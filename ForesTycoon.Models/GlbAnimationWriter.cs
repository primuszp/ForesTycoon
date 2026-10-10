using System;
using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using OpenTK.Mathematics;
namespace ForesTycoon.Models
{
    // Appends ordinary glTF TRS channels to the original asset. Blender and the game read the same GLB.
    internal static class GlbAnimationWriter
    {
        // Largest-diagonal conversion remains stable at the wrist's near-180-degree rotations.
        private static Quaternion Rotation(Matrix4 m, Vector3 scale)
        {
            m.Row0/=scale.X; m.Row1/=scale.Y; m.Row2/=scale.Z;
            float trace=m.M11+m.M22+m.M33,s;
            Quaternion q;
            if(trace>0){s=MathF.Sqrt(trace+1)*2;q=new((m.M23-m.M32)/s,(m.M31-m.M13)/s,(m.M12-m.M21)/s,s/4);}
            else if(m.M11>m.M22&&m.M11>m.M33){s=MathF.Sqrt(1+m.M11-m.M22-m.M33)*2;q=new(s/4,(m.M12+m.M21)/s,(m.M13+m.M31)/s,(m.M23-m.M32)/s);}
            else if(m.M22>m.M33){s=MathF.Sqrt(1+m.M22-m.M11-m.M33)*2;q=new((m.M12+m.M21)/s,s/4,(m.M23+m.M32)/s,(m.M31-m.M13)/s);}
            else{s=MathF.Sqrt(1+m.M33-m.M11-m.M22)*2;q=new((m.M13+m.M31)/s,(m.M23+m.M32)/s,s/4,(m.M12-m.M21)/s);}
            return q.Normalized();
        }
        internal static void Write(string source, string output, string name, AnimatedGlbModel model,
            float duration, int fps, Action<AnimatedGlbModel.Pose,float> sample, bool append = false)
        {
            byte[] bytes=File.ReadAllBytes(source);int jsonSize=BitConverter.ToInt32(bytes,12),binStart=28+jsonSize;
            var root=JsonNode.Parse(Encoding.UTF8.GetString(bytes,20,jsonSize)).AsObject();
            using var binary=new MemoryStream();binary.Write(bytes,binStart,bytes.Length-binStart);
            var views=root["bufferViews"].AsArray();var accessors=root["accessors"].AsArray();
            int Add(float[] values,int arity,string type,bool time=false) {
                while(binary.Length%4!=0)binary.WriteByte(0);int offset=(int)binary.Length;
                using(var writer=new BinaryWriter(binary,Encoding.UTF8,true))foreach(float v in values)writer.Write(v);
                int view=views.Count;views.Add(new JsonObject{["buffer"]=0,["byteOffset"]=offset,["byteLength"]=values.Length*4});
                var accessor=new JsonObject{["bufferView"]=view,["componentType"]=5126,["count"]=values.Length/arity,["type"]=type};
                if(time){accessor["min"]=new JsonArray(0);accessor["max"]=new JsonArray(duration);}
                int index=accessors.Count;accessors.Add(accessor);return index;
            }
            int count=(int)Math.Ceiling(duration*fps)+1;
            float[] times=new float[count];for(int i=0;i<count;i++)times[i]=Math.Min(duration,i/(float)fps);
            int input=Add(times,1,"SCALAR",true);
            var translations=new float[model.Nodes.Length][];var rotations=new float[model.Nodes.Length][];var scales=new float[model.Nodes.Length][];
            for(int n=0;n<model.Nodes.Length;n++){translations[n]=new float[count*3];rotations[n]=new float[count*4];scales[n]=new float[count*3];}
            var pose=model.CreatePose();
            for(int k=0;k<count;k++) {
                sample(pose,times[k]);
                for(int n=0;n<model.Nodes.Length;n++) {
                    if(model.Nodes[n].Matrix.HasValue)continue;
                    int parent=model.Nodes[n].Parent;
                    Matrix4 local=parent<0?pose.World[n]:pose.World[n]*pose.World[parent].Inverted();
                    Vector3 t=local.ExtractTranslation(),s=local.ExtractScale();Quaternion r=Rotation(local,s);
                    Matrix4 reconstructed=Matrix4.CreateScale(s)*Matrix4.CreateFromQuaternion(r)*Matrix4.CreateTranslation(t);
                    if((local.Row0-reconstructed.Row0).Length+(local.Row1-reconstructed.Row1).Length+(local.Row2-reconstructed.Row2).Length>.001f)
                        throw new InvalidOperationException($"Non-TRS pose: node {model.Nodes[n].Name}, time {times[k]}, scale {s}; local {local}; reconstructed {reconstructed}");
                    for(int j=0;j<3;j++){translations[n][k*3+j]=t[j];scales[n][k*3+j]=s[j];}
                    rotations[n][k*4]=r.X;rotations[n][k*4+1]=r.Y;rotations[n][k*4+2]=r.Z;rotations[n][k*4+3]=r.W;
                }
            }
            var samplers=new JsonArray();var channels=new JsonArray();
            void Channel(int node,string path,float[] values,int arity,string type) {
                int outputAccessor=Add(values,arity,type),sampler=samplers.Count;
                samplers.Add(new JsonObject{["input"]=input,["output"]=outputAccessor,["interpolation"]="LINEAR"});
                channels.Add(new JsonObject{["sampler"]=sampler,["target"]=new JsonObject{["node"]=node,["path"]=path}});
            }
            for(int n=0;n<model.Nodes.Length;n++)if(!model.Nodes[n].Matrix.HasValue) {
                Channel(n,"translation",translations[n],3,"VEC3");Channel(n,"rotation",rotations[n],4,"VEC4");Channel(n,"scale",scales[n],3,"VEC3");
            }
            if (!append || root["animations"] is not JsonArray) root["animations"] = new JsonArray();
            root["animations"].AsArray().Add(new JsonObject{["name"]=name,["samplers"]=samplers,["channels"]=channels});
            root["buffers"][0]["byteLength"]=(int)binary.Length;
            byte[] json=Encoding.UTF8.GetBytes(root.ToJsonString());int padded=(json.Length+3)&~3;
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output)));
            using var file=new BinaryWriter(File.Create(output));
            file.Write(0x46546c67);file.Write(2);file.Write(28+padded+(int)binary.Length);
            file.Write(padded);file.Write(0x4e4f534a);file.Write(json);for(int i=json.Length;i<padded;i++)file.Write((byte)32);
            file.Write((int)binary.Length);file.Write(0x004e4942);file.Write(binary.ToArray());
        }
    }
}
