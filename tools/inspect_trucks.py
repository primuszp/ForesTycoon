import struct,json,io
from pathlib import Path
import numpy as np
from PIL import Image
import matplotlib
matplotlib.use('Agg')
import matplotlib.pyplot as plt
from mpl_toolkits.mplot3d.art3d import Poly3DCollection
import sys
if len(sys.argv)!=2:raise SystemExit('Usage: python tools/inspect_trucks.py <trucks_collection.glb>')
src=Path(sys.argv[1])
b=src.read_bytes();n=struct.unpack_from('<I',b,12)[0];d=json.loads(b[20:20+n]);binary=b[28+n:]
def access(i):
 a=d['accessors'][i];v=d['bufferViews'][a['bufferView']];c={'SCALAR':1,'VEC2':2,'VEC3':3,'VEC4':4}[a['type']];ty={5126:'<f4',5125:'<u4',5123:'<u2'}[a['componentType']]
 return np.ndarray((a['count'],c),dtype=ty,buffer=binary,offset=v.get('byteOffset',0)+a.get('byteOffset',0),strides=(v.get('byteStride',np.dtype(ty).itemsize*c),np.dtype(ty).itemsize)).copy()
v=d['bufferViews'][d['images'][0]['bufferView']];atlas=np.array(Image.open(io.BytesIO(binary[v['byteOffset']:v['byteOffset']+v['byteLength']])).convert('RGB'))
def geometry(lo,hi):
 out=[]
 def walk(i,m):
  node=d['nodes'][i];mat=np.array(node.get('matrix',np.eye(4).T.reshape(-1).tolist())).reshape(4,4).T;m=m@mat
  if 'mesh' in node:
   for p in d['meshes'][node['mesh']]['primitives']:
    pos=access(p['attributes']['POSITION']);pos=(m@np.c_[pos,np.ones(len(pos))].T).T[:,:3]
    norm=access(p['attributes']['NORMAL']);norm=(np.linalg.inv(m[:3,:3]).T@norm.T).T;norm/=np.linalg.norm(norm,axis=1)[:,None]
    idx=access(p['indices']).reshape(-1)
    matl=d['materials'][p['material']];fac=matl.get('pbrMetallicRoughness',{}).get('baseColorFactor',[1,1,1,1])
    if 'TEXCOORD_0' in p['attributes'] and 'baseColorTexture' in matl.get('pbrMetallicRoughness',{}):
     uv=access(p['attributes']['TEXCOORD_0']);xy=np.clip((uv*np.array([atlas.shape[1],atlas.shape[0]])).astype(int),[0,0],[atlas.shape[1]-1,atlas.shape[0]-1]);color=atlas[xy[:,1],xy[:,0]]/255
    else:color=np.tile(fac[:3],(len(pos),1))
    out.append((node['name'],pos,norm,idx,color))
  for child in node.get('children',[]):walk(child,m)
 for i in d['nodes'][2]['children']:
  if lo<=i<hi:walk(i,np.eye(4))
 return out
groups=[(3,45,'Tanker'),(45,89,'Trailer2 + Prop1'),(89,127,'Trailer3'),(127,170,'Trailer1 + Prop2'),(170,210,'Chassis')]
fig=plt.figure(figsize=(16,7))
for j,(lo,hi,name) in enumerate(groups):
 ax=fig.add_subplot(2,3,j+1,projection='3d')
 for _,pos,norm,idx,color in geometry(lo,hi):
  pos=pos[:,[2,0,1]];faces=pos[idx.reshape(-1,3)];c=color[idx.reshape(-1,3)].mean(axis=1)
  shading=np.clip(.65+.35*(norm@np.array([.3,.8,.5])),.3,1)[idx.reshape(-1,3)].mean(axis=1)
  ax.add_collection3d(Poly3DCollection(faces,facecolors=c*shading[:,None],linewidths=0))
 allp=np.concatenate([g[1][:,[2,0,1]] for g in geometry(lo,hi)]);mn=allp.min(0);mx=allp.max(0)
 ax.set_xlim(mn[0],mx[0]);ax.set_ylim(mn[1],mx[1]);ax.set_zlim(mn[2],mx[2]);ax.set_box_aspect(mx-mn);ax.view_init(25,50);ax.set_axis_off();ax.set_title(name)
fig.tight_layout();fig.savefig('artifacts/graphics-weather/truck-collection-preview.png',dpi=130)


# Export just the log transporter as a compact, colour-preserving GLB.
parts=geometry(127,170)
expanded=[]
for name,pos,norm,idx,color in parts:
 if not name.startswith('Prop2'):
  expanded.append((name,pos,norm,idx,color));continue
 parent=list(range(len(pos)))
 def root(i):
  while parent[i]!=i:parent[i]=parent[parent[i]];i=parent[i]
  return i
 def join(a,b):parent[root(a)]=root(b)
 same={}
 for i,p in enumerate(pos):
  key=tuple(np.round(p,3))
  if key in same:join(i,same[key])
  else:same[key]=i
 for a,b,c in idx.reshape(-1,3):join(a,b);join(b,c)
 groups={}
 for tri in idx.reshape(-1,3):groups.setdefault(root(int(tri[0])),[]).extend(tri.tolist())
 for j,ids in enumerate(groups.values()):
  selected=np.unique(ids);mapping={int(old):new for new,old in enumerate(selected)}
  indices=np.array([mapping[i] for i in ids])
  expanded.append((name+'_log'+str(j),pos[selected],norm[selected],indices,color[selected]))
parts=[p for p in expanded if not p[0].startswith('Prop2')]+sorted([p for p in expanded if p[0].startswith('Prop2')],key=lambda p:float(p[1][:,1].mean()))
print('cargo pieces',sum(p[0].startswith('Prop2') for p in parts))
allp=np.concatenate([p[1][:,[2,0,1]] for p in parts]);mn=allp.min(0);mx=allp.max(0)
scale=3.3/(mx[0]-mn[0]);centre=np.array([(mn[0]+mx[0])/2,(mn[1]+mx[1])/2,mn[2]])
payload=bytearray();views=[];accessors=[];meshes=[];nodes=[]
def append(array,kind,component):
 while len(payload)%4:payload.append(0)
 raw=array.tobytes();views.append({'buffer':0,'byteOffset':len(payload),'byteLength':len(raw)})
 payload.extend(raw)
 a={'bufferView':len(views)-1,'componentType':component,'count':len(array),'type':kind}
 if kind=='VEC3':a.update(min=array.min(0).tolist(),max=array.max(0).tolist())
 accessors.append(a);return len(accessors)-1
for name,pos,norm,idx,color in parts:
 pos=((pos[:,[2,0,1]]-centre)*scale).astype('<f4');norm=norm[:,[2,0,1]].astype('<f4')
 colour=np.c_[np.power(color,2.2),np.ones(len(color))].astype('<f4')
 category='cargo' if name.startswith('Prop2') else 'wheel' if name.startswith('Truck_Wheel_') else 'body'
 pivot=((pos.min(0)+pos.max(0))/2).tolist()
 primitives={'attributes':{'POSITION':append(pos,'VEC3',5126),'NORMAL':append(norm,'VEC3',5126),'COLOR_0':append(colour,'VEC4',5126)},'indices':append(idx.astype('<u4').reshape(-1,1),'SCALAR',5125),'mode':4}
 meshes.append({'name':name,'primitives':[primitives]})
 nodes.append({'name':name,'mesh':len(meshes)-1,'extras':{'category':category,'pivot':pivot}})
output={'asset':{'version':'2.0','generator':'ForesTycoon selected trucks_collection log carrier'},'scene':0,'scenes':[{'nodes':list(range(len(nodes)))}],'nodes':nodes,'meshes':meshes,'buffers':[{'byteLength':len(payload)}],'bufferViews':views,'accessors':accessors}
j=json.dumps(output,separators=(',',':')).encode()
j+=b' '*((-len(j))%4);payload+=b'\0'*((-len(payload))%4)
glb=struct.pack('<III',0x46546c67,2,12+8+len(j)+8+len(payload))+struct.pack('<II',len(j),0x4e4f534a)+j+struct.pack('<II',len(payload),0x004e4942)+payload
dest=Path('ForesTycoon/Assets/Vehicles/log-truck.glb');dest.parent.mkdir(parents=True,exist_ok=True);dest.write_bytes(glb)
print('Selected log truck:',len(nodes),'parts,',sum(len(p[3])//3 for p in parts),'triangles;',len(glb),'bytes')
