from pathlib import Path
p=Path(__file__).with_name('build_blender.py')
s=p.read_text()
start=s.index('# Continuous analytic')
end=s.index('# Origin at support plane.')
s=s[:start]+"""# Nested outer/inner outlines produce a continuous rounded, non-intersecting shell.
verts=[];faces=[];N=128;M=32
for i in range(N):
 t=2*math.pi*i/N
 ox=.055*math.sin(t)*(1-.10*math.cos(t))+.006*math.cos(t)
 oz=.065+.065*math.cos(t)+.025*math.exp(-((t-math.pi)/.35)**2)-.010
 ix=.018*math.sin(t)+.004
 iz=.069+.025*math.cos(t)
 for j in range(M):
  a=2*math.pi*j/M
  q=(1+math.cos(a))*.5
  verts.append((ix*(1-q)+ox*q,.023*math.sin(a),max(0,iz*(1-q)+oz*q-.003)))
for i in range(N):
 for j in range(M):
  faces.append((i*M+j,((i+1)%N)*M+j,((i+1)%N)*M+(j+1)%M,i*M+(j+1)%M))
mesh=bpy.data.meshes.new('ContinuousCeramicMesh');mesh.from_pydata(verts,[],faces);mesh.update()
obj=bpy.data.objects.new('Ceramic_Study',mesh);scene.collection.objects.link(obj)
for polygon in mesh.polygons:polygon.use_smooth=True
obj.data.materials.append(ceramic);uv(obj)
""" +s[end:]
p.write_text(s)
