"""Author a continuous compartment in Blender; no separate display-box shell."""
import bpy, math
from mathutils import Vector, Matrix

WIDTH, HEIGHT, FRONT, BACK = 4.266666666666667, 2.4, -1.7, 2.74
X_SCALE = 1.0

def rebuild():
    scene=bpy.context.scene
    remove=('continuous walnut wall','raised field panel','vertical walnut stile',
            'raised panel border rail','side-wall moulding','brass wall inlay',
            'continuous ivory curved ceiling','arched ceiling rib','ceiling brass seam')
    for obj in list(scene.objects):
        if any(part in obj.name.lower() for part in remove):
            bpy.data.objects.remove(obj,do_unlink=True)
            continue
        # Original furnishings retain their exact transform and proportions.
    mats=bpy.data.materials
    wood=mats['V3 cared-for walnut | panel | YZ']
    trim=mats['V3 cared-for walnut | trim | YZ']
    brass=mats['Aged satin brass edges']
    ivory=mats['Aged ivory enamel ceiling']
    def mesh(name,verts,faces,mat):
        data=bpy.data.meshes.new(name);data.from_pydata(verts,[],faces);data.update()
        obj=bpy.data.objects.new(name,data);scene.collection.objects.link(obj);data.materials.append(mat)
        return obj
    def cube(name,center,size,mat,bevel=.006):
        bpy.ops.mesh.primitive_cube_add(size=1,location=center)
        obj=bpy.context.object;obj.name=name;obj.dimensions=size
        bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
        obj.data.materials.append(mat)
        if bevel:
            mod=obj.modifiers.new('Soft crafted edges','BEVEL');mod.width=bevel;mod.segments=2
            obj.modifiers.new('Weighted panel normals','WEIGHTED_NORMAL')
        return obj
    def curve(name,points,mat,radius=.007):
        data=bpy.data.curves.new(name,'CURVE');data.dimensions='3D';data.resolution_u=1
        data.bevel_depth=radius;data.bevel_resolution=2
        spline=data.splines.new('POLY');spline.points.add(len(points)-1)
        for p,co in zip(spline.points,points):p.co=(*co,1)
        obj=bpy.data.objects.new(name,data);scene.collection.objects.link(obj);data.materials.append(mat)
    # Foreground wall returns join the original-width compartment continuously.
    # This changes architecture, never stretches the original furniture.
    def half_width(y):
        return WIDTH/2-(WIDTH/2-1.65)*min(1,max(0,(y-FRONT)/1.4))
    def wall_piece(name,side,y,length,z,thickness,height,material,inset):
        a=y-length/2;b=y+length/2
        dx=side*(half_width(b)-half_width(a));angle=math.atan2(-dx,length)
        obj=cube(name,(side*(half_width(y)-inset),y,z),(thickness,math.hypot(dx,length),height),material,.003)
        obj.rotation_euler.z=angle
        return obj
    # Solid structural walls, continuing all the way to the fixed display plane.
    for side in (-1,1):
        for a,b in [(FRONT,-.3),(-.3,BACK)]:
            verts=[(side*half_width(a),a,0),(side*half_width(b),b,0),(side*half_width(b),b,HEIGHT),(side*half_width(a),a,HEIGHT)]
            mesh(('Left' if side<0 else 'Right')+' continuous cabin wall',verts,[(0,1,2,3) if side<0 else (3,2,1,0)],wood)
        # Raised walnut panels include the nearest foreground bays, not just the far wall.
        for bay in range(8):
            y=FRONT+.035+(BACK-FRONT-.07)*(bay+.5)/8
            length=(BACK-FRONT-.07)/8-.026
            wall_piece('Walnut wall panel complete %s %s'%(side,bay),side,y,length,1.14,.028,1.64,wood,.022)
            for edge in (-1,1):
                yy=y+edge*length/2
                wall_piece('Panel stile complete',side,yy,.022,1.14,.024,1.67,trim,.046)
            for z in (.32,1.96):
                wall_piece('Panel border complete',side,y,length,z,.024,.024,trim,.046)
        for z,thick in [(.10,.16),(1.43,.045)]:
            for a,b in [(FRONT+.03,-.3),(-.3,BACK)]:wall_piece('Continuous cabin moulding',side,(a+b)/2,b-a,z,.085,thick,trim,.063)
        for z in (.22,1.467):
            for a,b in [(FRONT+.03,-.3),(-.3,BACK)]:wall_piece('Continuous brass inlay',side,(a+b)/2,b-a,z,.007,.006,brass,.108)
    # A single continuous ivory ceiling: flush at the display's upper edge,
    # transitioning into the carriage's shallow barrel vault. No nested roof.
    def roof(x,y):
        blend=min(1,max(0,(y-FRONT)/1.0));blend=blend*blend*(3-2*blend)
        return HEIGHT-(.13+.237*(x/half_width(y))**2)*blend
    nx,ny=48,20
    verts=[]
    ys=sorted(set([FRONT+(BACK-FRONT)*j/ny for j in range(ny+1)]+[-.3]))
    ny=len(ys)-1
    for y in ys:
        for i in range(nx+1):
            x=half_width(y)*(2*i/nx-1);verts.append((x,y,roof(x,y)))
    faces=[]
    for j in range(ny):
        for i in range(nx):
            a=j*(nx+1)+i;faces.append((a,a+nx+1,a+nx+2,a+1))
    ceiling=mesh('Continuous ivory cabin ceiling',verts,faces,ivory)
    for p in ceiling.data.polygons:p.use_smooth=True
    for y in (-1.15,-.25,.65,1.55,2.5):
        curve('Continuous ceiling brass rib',[(x,y,roof(x,y)-.012) for x in [-half_width(y)+.02+(2*half_width(y)-.04)*i/48 for i in range(49)]],brass,.006)
    for side in (-1,1):
        ys_trim=[FRONT+.04+(BACK-FRONT-.08)*i/80 for i in range(81)]
        curve('Continuous curved cabin cornice',[(side*(half_width(y)-.04),y,roof(side*half_width(y),y)-.03) for y in ys_trim],trim,.022)
    # Close the full rear bulkhead, leaving only the actual glazed opening.
    wx=1.04*X_SCALE
    for x0,x1,z0,z1 in [(-WIDTH/2,-wx,0,HEIGHT),(wx,WIDTH/2,0,HEIGHT),(-wx,wx,0,.755),(-wx,wx,1.965,HEIGHT)]:
        mesh('Complete rear walnut bulkhead',[(x0,2.72,z0),(x0,2.72,z1),(x1,2.72,z1),(x1,2.72,z0)],[(3,2,1,0)],wood)
    # Illumination is motivated by the visible reading lights and overcast window.
    # Do not add floating foreground light pools to the newly completed walls.
    energies={'Warm reflected ceiling fill':2,
              'Left 2700K practical':34,'Right 2700K practical':34,
              'Left practical wall bounce':1.8,'Right practical wall bounce':1.8,
              'Cool daylight entering the central window':25,
              'Restrained cool camera-side bounce':55,
              'Overcast exterior illumination':420}
    for obj in scene.objects:
        if obj.type=='LIGHT' and obj.name in energies:
            obj.data.energy=energies[obj.name]
            if obj.name=='Restrained cool camera-side bounce':
                obj.data.shape='DISK';obj.data.size=4.5
    if scene.world and scene.world.use_nodes:
        for node in scene.world.node_tree.nodes:
            if node.type=='BACKGROUND':node.inputs['Strength'].default_value*=.4
    scene.view_settings.exposure-=.25
    scene['train_structure_revision']='complete-compartment-A-3-low-light'
    scene['train_display_volume']=[WIDTH,HEIGHT,6.0]
    scene['train_display_front_y']=FRONT
