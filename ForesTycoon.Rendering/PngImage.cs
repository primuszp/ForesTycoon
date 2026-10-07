using System;
using System.IO;
using System.IO.Compression;
using System.Buffers.Binary;
namespace ForesTycoon.Rendering
{
    // Embedded non-interlaced PNG textures, including palette/tRNS alpha.
    internal readonly record struct PngImage(int Width, int Height, byte[] Pixels)
    {
        internal static PngImage Decode(byte[] png)
        {
            if (png.Length < 33 || !png.AsSpan(0,8).SequenceEqual(new byte[] {137,80,78,71,13,10,26,10}))
                throw new InvalidDataException("Invalid PNG.");
            int width=0,height=0,channels=0,depth=0,colorType=0;
            byte[] palette=null,transparency=null;
            using var compressed=new MemoryStream();
            for(int offset=8;offset+12<=png.Length;)
            {
                int size=BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(offset,4));
                if(size<0 || size>png.Length-offset-12) throw new InvalidDataException("PNG chunk bounds.");
                string type=System.Text.Encoding.ASCII.GetString(png,offset+4,4);
                if(type=="IHDR") {
                    if(size!=13) throw new InvalidDataException("PNG header.");
                    width=BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(offset+8,4));
                    height=BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(offset+12,4));
                    depth=png[offset+16];colorType=png[offset+17];
                    channels=colorType switch {0=>1,2=>3,3=>1,4=>2,6=>4,_=>0};
                    bool validDepth=depth==8||((colorType==0||colorType==3)&&(depth==1||depth==2||depth==4));
                    if(width<1||height<1||width>4096||height>4096||channels==0||!validDepth||png[offset+18]!=0||png[offset+19]!=0||png[offset+20]!=0)
                        throw new InvalidDataException("Unsupported PNG depth, colour type or interlacing.");
                }
                if(type=="PLTE") {
                    if(size<3||size>768||size%3!=0)throw new InvalidDataException("PNG palette.");
                    palette=png.AsSpan(offset+8,size).ToArray();
                }
                if(type=="tRNS")transparency=png.AsSpan(offset+8,size).ToArray();
                if(type=="IDAT") compressed.Write(png,offset+8,size);
                offset+=size+12;
            }
            if(channels==0) throw new InvalidDataException("Missing PNG header.");
            if(colorType==3&&(palette==null||(transparency!=null&&transparency.Length>palette.Length/3)))throw new InvalidDataException("PNG palette alpha.");
            if(transparency!=null&&((colorType==0&&transparency.Length!=2)||(colorType==2&&transparency.Length!=6)||colorType==4||colorType==6))throw new InvalidDataException("PNG transparency.");
            compressed.Position=0;
            int stride=checked((width*channels*depth+7)/8),bytesPerPixel=Math.Max(1,channels*depth/8);
            byte[] raw=new byte[checked((stride+1)*height)], pixels=new byte[checked(width*height*4)];
            using(var stream=new ZLibStream(compressed,CompressionMode.Decompress)) stream.ReadExactly(raw);
            byte[] previous=new byte[stride], row=new byte[stride];
            for(int y=0;y<height;y++) {
                int start=y*(stride+1); byte filter=raw[start];
                for(int x=0;x<stride;x++) {
                    int a=x>=bytesPerPixel?row[x-bytesPerPixel]:0,b=previous[x],c=x>=bytesPerPixel?previous[x-bytesPerPixel]:0;
                    int prediction=filter switch {0=>0,1=>a,2=>b,3=>(a+b)/2,4=>Paeth(a,b,c),_=>throw new InvalidDataException("PNG filter.")};
                    row[x]=unchecked((byte)(raw[start+1+x]+prediction));
                }
                for(int x=0;x<width;x++) {
                    int target=(y*width+x)*4,source=x*channels;
                    pixels[target+3]=255;
                    if(colorType==0||colorType==3) {
                        int sample=depth==8?row[x]:(row[x*depth/8]>>(8-depth-x*depth%8))&((1<<depth)-1);
                        if(colorType==3) {
                            if(sample>=palette.Length/3)throw new InvalidDataException("PNG palette index.");
                            Array.Copy(palette,sample*3,pixels,target,3);
                            if(transparency!=null&&sample<transparency.Length)pixels[target+3]=transparency[sample];
                        } else {
                            pixels[target]=pixels[target+1]=pixels[target+2]=(byte)(sample*255/((1<<depth)-1));
                            if(transparency!=null&&sample==BinaryPrimitives.ReadUInt16BigEndian(transparency))pixels[target+3]=0;
                        }
                    } else if(colorType==4) {
                        pixels[target]=pixels[target+1]=pixels[target+2]=row[source];pixels[target+3]=row[source+1];
                    } else {
                        pixels[target]=row[source];pixels[target+1]=row[source+1];pixels[target+2]=row[source+2];
                        if(colorType==6)pixels[target+3]=row[source+3];
                        else if(transparency!=null&&row[source]==BinaryPrimitives.ReadUInt16BigEndian(transparency)
                            &&row[source+1]==BinaryPrimitives.ReadUInt16BigEndian(transparency.AsSpan(2))
                            &&row[source+2]==BinaryPrimitives.ReadUInt16BigEndian(transparency.AsSpan(4)))pixels[target+3]=0;
                    }
                }
                (row,previous)=(previous,row);
            }
            return new PngImage(width,height,pixels);
        }
        private static int Paeth(int a,int b,int c) {
            int p=a+b-c,pa=Math.Abs(p-a),pb=Math.Abs(p-b),pc=Math.Abs(p-c);
            return pa<=pb&&pa<=pc?a:pb<=pc?b:c;
        }
    }
}
