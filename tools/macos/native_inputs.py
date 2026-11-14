"""Validate Mac-produced Universal native inputs before staging a cross-build."""
import hashlib
import json
from pathlib import Path
import shutil
import struct

SOURCES=('tools/macos/SpatialMac.m','tools/macos/SpatialCamera.m')
DESTINATIONS={
 'libSpatialMac.dylib':'Prototype/Assets/Plugins/macOS/libSpatialMac.dylib',
 'libSpatialCamera.dylib':'Validation/Desktop/native/libSpatialCamera.dylib',
}

def sha256(path):
 h=hashlib.sha256()
 with Path(path).open('rb') as stream:
  for block in iter(lambda:stream.read(4*1024*1024),b''):h.update(block)
 return h.hexdigest()

def mach_o_architectures(path):
 names={0x1000007:'x86_64',0x100000c:'arm64'}
 size=Path(path).stat().st_size
 with Path(path).open('rb') as stream:
  def slice_cpu(offset,length):
   if length<32 or offset<0 or offset+length>size:raise ValueError('invalid Mach-O slice range')
   stream.seek(offset);header=stream.read(32)
   if header[:4] not in (b'\xcf\xfa\xed\xfe',b'\xfe\xed\xfa\xcf'):raise ValueError('invalid Mach-O slice header')
   endian='<' if header[0]==0xcf else '>'
   cpu,_,_,_,commands=struct.unpack(endian+'IIIII',header[4:24])
   if 32+commands>length:raise ValueError('truncated Mach-O slice load commands')
   return cpu
  magic=stream.read(4)
  if magic in (b'\xca\xfe\xba\xbe',b'\xca\xfe\xba\xbf',b'\xbe\xba\xfe\xca',b'\xbf\xba\xfe\xca'):
   endian='>' if magic[0]==0xca else '<';wide=magic in (b'\xca\xfe\xba\xbf',b'\xbf\xba\xfe\xca')
   raw_count=stream.read(4)
   if len(raw_count)!=4:raise ValueError('truncated Mach-O architecture table')
   count=struct.unpack(endian+'I',raw_count)[0]
   if count not in (1,2):raise ValueError('unsupported Mach-O architecture count')
   slices=[];table_end=8+count*(32 if wide else 20)
   for _ in range(count):
    entry=stream.read(32 if wide else 20)
    if len(entry)!=(32 if wide else 20):raise ValueError('truncated Mach-O architecture table')
    if wide:cpu,_,offset,length,_,_=struct.unpack(endian+'IIQQII',entry)
    else:cpu,_,offset,length,_=struct.unpack(endian+'IIIII',entry)
    if offset<table_end:raise ValueError('Mach-O slice overlaps architecture table')
    if any(offset<other_offset+other_length and other_offset<offset+length for _,other_offset,other_length in slices):raise ValueError('overlapping Mach-O slices')
    slices.append((cpu,offset,length))
   for cpu,offset,length in slices:
    if slice_cpu(offset,length)!=cpu:raise ValueError('Mach-O slice CPU does not match architecture table')
   return [names.get(cpu,'unsupported') for cpu,_,_ in slices]
  if magic in (b'\xcf\xfa\xed\xfe',b'\xfe\xed\xfa\xcf'):
   return [names.get(slice_cpu(0,size),'unsupported')]
  raise ValueError('not a Mach-O library: '+str(path))

def write_native_manifest(root,directory):
 root=Path(root);directory=Path(directory)
 manifest={'schema':1,'sources':{p:sha256(root/p) for p in SOURCES},'files':{}}
 for name in DESTINATIONS:
  p=directory/name;manifest['files'][name]={'sha256':sha256(p),'architectures':mach_o_architectures(p)}
 (directory/'native-manifest.json').write_text(json.dumps(manifest,indent=2)+'\n',encoding='utf8')
 return manifest

def prepare_native_inputs(root,directory):
 root=Path(root);directory=Path(directory)
 manifest=json.loads((directory/'native-manifest.json').read_text(encoding='utf8'))
 if manifest.get('schema')!=1:raise ValueError('unsupported native manifest schema')
 for source in SOURCES:
  if manifest['sources'].get(source)!=sha256(root/source):raise ValueError('native source mismatch: '+source)
 for name in DESTINATIONS:
  record=manifest['files'].get(name);p=directory/name
  if record is None or record['sha256']!=sha256(p):raise ValueError('native library hash mismatch: '+name)
  if set(mach_o_architectures(p))!={'arm64','x86_64'}:raise ValueError('native library architectures must include arm64 and x86_64: '+name)
 for name,relative in DESTINATIONS.items():
  source=directory/name;target=root/relative;target.parent.mkdir(parents=True,exist_ok=True)
  if source.resolve()!=target.resolve():shutil.copyfile(source,target)
 return manifest
