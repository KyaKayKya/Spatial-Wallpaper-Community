from fixtures import sample,ins
def create():
 p,c=sample();cls=p['classes'][0];fields=c['nodes'][0]['components'][1]['fields']
 for name,typ,value in [('flag','bool',False),('ratio','f32',0.),('arithmetic','i32',0),('text','string',None),('items','i32[]',None),('vector','vector3',[0,0,0]),('color','color',[0,0,0,0]),('data','Data',None),('list','list_i32',None)]:
  cls['fields'].append(dict(id=name,type=typ));fields[name]=value
 p['classes'].append(dict(id='Data',fields=[dict(id='v',type='i32')],methods=[]))
 cls['methods'].append(dict(id='Helper',returns='i32',parameters=['i32'],locals=[],body=[ins('ldarg',1),ins('ldc.i4',2),ins('mul'),ins('ret')]))
 body=[];labels={};branches=[]
 def emit(op,value=None):body.append(ins(op,value))
 def label(name):labels[name]=len(body)
 def branch(op,name):branches.append((len(body),name));emit(op,0)
 def load(name):emit('ldarg',0);emit('ldfield',name)
 emit('ldarg',0)
 for op,value in [('ldc.i4',7),('ldc.i4',3),('and',None),('ldc.i4',2),('xor',None),('ldc.i4',8),('or',None),('not',None),('neg',None),('stfield','counter')]:emit(op,value)
 emit('ldarg',0)
 for op,value in [('ldc.i4',7),('ldc.i4',5),('add',None),('ldc.i4',3),('mul',None),('ldc.i4',8),('sub',None),('ldc.i4',4),('div',None),('ldc.i4',5),('rem',None),('stfield','arithmetic')]:emit(op,value)
 emit('ldarg',0);emit('ldc.i4',9);emit('conv.r4');emit('ldc.r4',2.);emit('div');emit('stfield','ratio')
 emit('ldc.r4',2.5);emit('conv.i4');emit('pop');emit('ldc.r4',-2.);emit('neg');emit('pop')
 emit('ldc.i4',1);emit('dup');emit('pop');emit('pop')
 emit('ldarg',0);emit('ldc.i4',1);emit('conv.bool');emit('ldc.i4',0);emit('ceq');branch('brfalse','true')
 emit('ldc.i4',0);branch('br','joined');label('true');emit('ldc.i4',1);label('joined');emit('conv.bool')
 emit('ldc.i4',2);emit('ldc.i4',3);emit('clt');emit('and');emit('ldc.i4',3);emit('ldc.i4',2);emit('cgt');emit('and')
 emit('ldc.i4',0);emit('conv.bool');emit('or');emit('ldc.i4',0);emit('conv.bool');emit('xor');emit('stfield','flag')
 emit('ldarg',0);emit('ldnull');emit('stfield','text');emit('ldarg',0);emit('ldstr','Sdk');emit('stfield','text')
 emit('ldarg',0);emit('ldc.i4',2);emit('newarray','i32');emit('stfield','items')
 load('items');emit('ldc.i4',0);emit('ldarg',0);emit('ldc.i4',5);emit('invoke','Demo.Helper');emit('stelem','i32')
 load('items');emit('ldlen');emit('stloc',1)
 emit('ldc.i4',3);emit('stloc',0);label('loop');emit('nop');emit('ldloc',0);emit('ldc.i4',1);emit('sub');emit('stloc',0);emit('ldloc',0);branch('brtrue','loop')
 load('items');emit('ldc.i4',1);emit('ldloc',0);emit('stelem','i32');load('items');emit('ldc.i4',0);emit('ldelem','i32');emit('pop')
 emit('ldarg',0)
 for v in [1.,2.,3.]:emit('ldc.r4',v)
 emit('newvector3');emit('stfield','vector');emit('ldarg',0)
 for v in [.1,.2,.3,.4]:emit('ldc.r4',v)
 emit('newcolor');emit('stfield','color')
 emit('ldarg',0);emit('newobj','Data');emit('dup');emit('ldc.i4',3);emit('stfield','v');emit('stfield','data')
 emit('ldarg',0);emit('ldc.i4',1);emit('newlist','list_i32');emit('stfield','list')
 prefix='mscorlib::System.Collections.Generic.List<System.Int32>::'
 for member,args in [('Add(System.Int32)->System.Void',[7]),('get_Count()->System.Int32',[]),('get_Item(System.Int32)->System.Int32',[0]),('set_Item(System.Int32,System.Int32)->System.Void',[0,9]),('Clear()->System.Void',[]),('Add(System.Int32)->System.Void',[2])]:
  load('list')
  for v in args:emit('ldc.i4',v)
  emit('call',prefix+member)
  if 'get_' in member:emit('pop')
 emit('ret')
 for index,name in branches:body[index]['value']=labels[name]
 cls['methods'][0]['locals']=['i32','i32'];cls['methods'][0]['body']=body
 return p,c
