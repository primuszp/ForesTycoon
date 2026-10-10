using System;
using OpenTK.Mathematics;

namespace ForesTycoon.Models
{
    internal sealed partial class AnimatedGlbModel
    {
        internal enum AnimationPath { Translation, Rotation, Scale }
        internal enum AnimationInterpolation { Linear, Step, CubicSpline }
        internal sealed class Channel {
            internal int Node, Arity;
            internal AnimationPath Path;
            internal AnimationInterpolation Interpolation;
            internal float[] Times, Values;
            internal Vector4 Sample(float time) {
                if (!float.IsFinite(time)) throw new ArgumentOutOfRangeException(nameof(time));
                int index=Array.BinarySearch(Times,time); if(index<0) index=Math.Max(0,~index-1);
                int next=Math.Min(index+1,Times.Length-1);
                float span=Times[next]-Times[index],t=span>0?Math.Clamp((time-Times[index])/span,0,1):0;
                int stride=Interpolation==AnimationInterpolation.CubicSpline?3:1;
                Vector4 Read(int frame,int slot=0) {
                    int offset=(frame*stride+slot)*Arity;
                    return new Vector4(Values[offset],Arity>1?Values[offset+1]:0,Arity>2?Values[offset+2]:0,Arity>3?Values[offset+3]:0);
                }
                Vector4 a=Read(index,stride==3?1:0),b=Read(next,stride==3?1:0);
                if(Interpolation==AnimationInterpolation.Step||index==next)return a;
                if(stride==3) {
                    float t2=t*t,t3=t2*t;
                    Vector4 result=(2*t3-3*t2+1)*a+(t3-2*t2+t)*span*Read(index,2)+(-2*t3+3*t2)*b+(t3-t2)*span*Read(next);
                    return Path==AnimationPath.Rotation?QuaternionVector(new Quaternion(result.X,result.Y,result.Z,result.W).Normalized()):result;
                }
                if(Path==AnimationPath.Rotation)return QuaternionVector(Quaternion.Slerp(new Quaternion(a.X,a.Y,a.Z,a.W).Normalized(),
                    new Quaternion(b.X,b.Y,b.Z,b.W).Normalized(),t));
                return Vector4.Lerp(a,b,t);
            }
        }
        internal sealed class Pose
        {
            internal readonly Matrix4[] World;
            private readonly Vector3[] translations,scales,otherTranslations,otherScales;
            private readonly Quaternion[] rotations,otherRotations;
            private readonly AnimatedGlbModel model;
            internal AnimatedGlbModel Model => model;
            internal Pose(AnimatedGlbModel model) {
                this.model=model;int n=model.Nodes.Length;World=new Matrix4[n];
                translations=new Vector3[n];scales=new Vector3[n];rotations=new Quaternion[n];
                otherTranslations=new Vector3[n];otherScales=new Vector3[n];otherRotations=new Quaternion[n];
            }
            internal void Evaluate(string clip,double time,string other=null,double otherTime=0,float blend=0,int inPlaceRoot=-1) {
                if (!double.IsFinite(time)) throw new ArgumentOutOfRangeException(nameof(time));
                if (!double.IsFinite(otherTime)) throw new ArgumentOutOfRangeException(nameof(otherTime));
                if (!float.IsFinite(blend) || blend < 0 || blend > 1) throw new ArgumentOutOfRangeException(nameof(blend));
                if (inPlaceRoot < -1 || inPlaceRoot >= World.Length) throw new ArgumentOutOfRangeException(nameof(inPlaceRoot));
                // Crossfade endpoints need only one clip, with no per-node blending.
                if (other != null && blend == 1)
                    Sample(other,otherTime,translations,scales,rotations,inPlaceRoot);
                else
                    Sample(clip,time,translations,scales,rotations,inPlaceRoot);
                if(other!=null&&blend>0&&blend<1) {
                    Sample(other,otherTime,otherTranslations,otherScales,otherRotations,inPlaceRoot);
                    for(int i=0;i<World.Length;i++) {
                        translations[i]=Vector3.Lerp(translations[i],otherTranslations[i],blend);
                        scales[i]=Vector3.Lerp(scales[i],otherScales[i],blend);
                        rotations[i]=Quaternion.Slerp(rotations[i],otherRotations[i],blend);
                    }
                }
                foreach(int i in model.Order) {
                    Matrix4 local=model.Nodes[i].Matrix??Matrix4.CreateScale(scales[i])*Matrix4.CreateFromQuaternion(rotations[i])*Matrix4.CreateTranslation(translations[i]);
                    World[i]=model.Nodes[i].Parent<0?local:local*World[model.Nodes[i].Parent];
                }
            }
            private void Sample(string name,double time,Vector3[] t,Vector3[] s,Quaternion[] r,int inPlaceRoot) {
                for(int i=0;i<World.Length;i++){t[i]=model.Nodes[i].Translation;s[i]=model.Nodes[i].Scale;r[i]=model.Nodes[i].Rotation;}
                if(name==null)return;
                if(!model.Clips.TryGetValue(name,out var clip))throw new ArgumentException("Unknown animation: "+name);
                float wrapped=clip.Duration>0?(float)((time%clip.Duration+clip.Duration)%clip.Duration):0;
                foreach(var channel in clip.Channels) {
                    Vector4 v=channel.Sample(channel.Path==AnimationPath.Translation&&channel.Node==inPlaceRoot?0:wrapped);
                    // Freeze only locomotion translation before blending. Limb motion and
                    // pelvis bob remain animated; world movement belongs to the simulation.
                    if(channel.Path==AnimationPath.Translation)t[channel.Node]=v.Xyz;
                    else if(channel.Path==AnimationPath.Rotation)r[channel.Node]=new Quaternion(v.X,v.Y,v.Z,v.W).Normalized();
                    else s[channel.Node]=v.Xyz;
                }
            }
            internal Matrix4 JointMatrix(int skin,int joint)=>model.Skins[skin].InverseBind[joint]*World[model.Skins[skin].Joints[joint]];
        }
    }
}
