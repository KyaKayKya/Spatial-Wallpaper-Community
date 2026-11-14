"""Mutate real pinned shader payload fields; never a production admission parser."""
from pathlib import Path
import struct
import zipfile

class Reader:
    def __init__(self,data,endian='<'):
        self.data=data;self.p=0;self.endian=endian
    def number(self,code):
        size=struct.calcsize(code);value=struct.unpack_from(self.endian+code,self.data,self.p)[0];self.p+=size;return value
    def take(self,n):
        assert 0<=n<=len(self.data)-self.p
        data=self.data[self.p:self.p+n];self.p+=n;return data
    def align(self,n):self.p=(self.p+n-1)//n*n
    def string(self):
        end=self.data.index(0,self.p);data=self.data[self.p:end].decode();self.p=end+1;return data

def lz4(source,size):
    r=Reader(source);data=bytearray()
    def length(v):
        if v==15:
            while True:
                a=r.number('B');v+=a
                if a!=255:break
        return v
    while r.p<len(source):
        t=r.number('B');data+=r.take(length(t>>4))
        if r.p==len(source):break
        distance=r.number('H');n=length(t&15)+4
        for _ in range(n):data.append(data[-distance])
    assert len(data)==size
    return bytes(data)

def generate(package,out):
    out.mkdir(parents=True,exist_ok=True)
    with zipfile.ZipFile(package) as z:
        key=next(n for n in z.namelist() if n.endswith('.shaderbundle'))
        bundle=z.read(key)
    r=Reader(bundle,'>');assert r.string()=='UnityFS';r.number('I');r.string();r.string();r.number('Q');packed=r.number('I');expanded=r.number('I');r.number('I');r.align(16)
    meta=Reader(lz4(r.take(packed),expanded),'>');r.align(16);base=r.p
    meta.take(16)
    for _ in range(meta.number('I')):meta.take(10)
    assert meta.number('I')==1;assert meta.number('Q')==0
    size=meta.number('Q');meta.number('I');meta.string()
    s=Reader(bundle[base:base+size],'>');s.take(20);metadata_size_pos=s.p;s.number('I');s.number('Q');data_offset=s.number('Q');s.number('Q');s.endian='<';s.string();s.take(5)
    types=[];class_pos=[];tree_pos=[]
    for _ in range(s.number('I')):
        class_pos.append(s.p);kind=s.number('i');s.take(19);tree_pos.append(s.p);count=s.number('I');string_size=s.number('I');raw=s.take(count*32);strings=s.take(string_size)
        def text(offset):
            if offset&0x80000000:return '@'+str(offset&0x7fffffff)
            return strings[offset:strings.index(0,offset)].decode()
        nodes=[]
        for i in range(count):
            ver,level,flag,ty,name,byte_size,index,flags,ref=struct.unpack_from('<HBBIIiiIQ',raw,i*32)
            nodes.append({'level':level,'type':text(ty),'name':text(name),'size':byte_size,'flags':flags,'children':[]})
        stack=[]
        for n in nodes:
            while stack and stack[-1]['level']>=n['level']:stack.pop()
            if stack:stack[-1]['children'].append(n)
            stack.append(n)
        s.take(s.number('I')*4);types.append((kind,nodes[0]))
    objects=[]
    for _ in range(s.number('I')):
        s.align(4);s.number('q');offset=s.number('q');length=s.number('I');typ=s.number('I');objects.append((data_offset+offset,length,typ))
    fields=[];sections={}
    def walk(n,payload,object_base,path):
        start=payload.p;name=n['name'];path=path+'/'+name
        if n['type'].startswith('PPtr<') or n['type'] in ['@633','@688']:
            fields.append(('pointer',object_base+start,12));payload.take(12)
        elif n['type']=='@840':payload.take(payload.number('I'));payload.align(4)
        elif n['type']=='@49':
            count=payload.number('I');fields.append((path+'/count',object_base+start,4));item=n['children'][1]
            if not item['children'] and item['size']==1:payload.take(count)
            else:
                for _ in range(count):walk(item,payload,object_base,path)
        elif n['children']:
            for child in n['children']:walk(child,payload,object_base,path)
        else:payload.take(n['size']);fields.append((path,object_base+start,n['size']))
        if n['flags']&0x4000:payload.align(4)
        if n['level']==1:sections[n['name']]=(object_base+start,payload.p-start)
    for offset,length,typ in objects:
        payload=Reader(s.data[offset:offset+length]);walk(types[typ][1],payload,base+offset,'');assert payload.p==length
        if types[typ][0]==48:
            def matrix(name):
                start,n=sections[name];r=Reader(bundle[start:start+n]);return [[r.number('I') for _ in range(r.number('I'))] for _ in range(r.number('I'))]
            offsets=matrix('offsets');packed=matrix('compressedLengths');expanded=matrix('decompressedLengths')
            start,n=sections['compressedBlob'];blob_reader=Reader(bundle[start:start+n]);blob=blob_reader.take(blob_reader.number('I'))
            for i,row in enumerate(offsets):
                for j,position in enumerate(row):
                    program=lz4(blob[position:position+packed[i][j]],expanded[i][j])
                    (out/f'program-{typ}-{i}-{j}.bin').write_bytes(program)
    attacks={}
    def patch(name,offset,value):
        b=bytearray(bundle);b[offset:offset+len(value)]=value;attacks[name]=bytes(b)
    patch('forbidden-class',base+class_pos[0],struct.pack('<i',114))
    patch('altered-layout',base+tree_pos[0]+8,b'\xff')
    old=struct.unpack_from('>I',bundle,base+metadata_size_pos)[0];patch('metadata-end',base+metadata_size_pos,struct.pack('>I',old-1))
    pointer=next(f for f in fields if f[0]=='pointer')
    patch('external-pointer',pointer[1],struct.pack('<i',1))
    patch('unregistered-pointer',pointer[1]+4,struct.pack('<q',9223372036854775807))
    scene=next(f for f in fields if f[0].endswith('/m_IsStreamedSceneAssetBundle'))
    patch('scene-configuration',scene[1],b'\x01')
    for name,suffix in [('shader-expansion','/decompressedLengths/@49/@106/@49/@106'),('shader-offset','/offsets/@49/@106/@49/@106'),('shader-size','/compressedLengths/@49/@106/@49/@106')]:
        f=next(f for f in fields if f[0].endswith(suffix));patch(name,f[1],struct.pack('<I',0x7fffffff))
    for name,data in attacks.items():(out/(name+'.shaderbundle')).write_bytes(data)
    print('Generated',len(attacks),'real native payload attacks',flush=True)

if __name__=='__main__':
    import sys
    generate(Path(sys.argv[1]),Path(sys.argv[2]))
