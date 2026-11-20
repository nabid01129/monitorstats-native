using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using MonitorStatsNative;

// The sensor service's RPC schema is architecture independent. Read the
// published sensor messages without loading the x64-only FanControl.IPC facade.
internal static partial class FanControlReader {
    static bool nativePipeOnly;
    static object nativeChannel,nativeMethod;
    static MethodInfo nativeRead,nativeDeadline;
    static Type nativeOptionsType;

    static void OpenNativePipe() {
        if(nativeChannel!=null)return;
        installPath=FindInstallPath();
        if(installPath==null)return;
        if(!resolverRegistered){AppDomain.CurrentDomain.AssemblyResolve+=ResolveFanControlAssembly;resolverRegistered=true;}
        var grpc=Assembly.LoadFrom(Path.Combine(installPath,"Grpc.Core.Api.dll"));
        var pipes=Assembly.LoadFrom(Path.Combine(installPath,"GrpcDotNetNamedPipes.dll"));
        var channelType=pipes.GetType("GrpcDotNetNamedPipes.NamedPipeChannel",true);
        nativeChannel=Activator.CreateInstance(channelType,new object[]{".","FanControl"});
        var marshallerType=grpc.GetType("Grpc.Core.Marshaller`1",true).MakeGenericType(typeof(byte[]));
        Func<byte[],byte[]> identity=bytes=>bytes;
        object marshaller=Activator.CreateInstance(marshallerType,new object[]{identity,identity});
        var methodType=grpc.GetType("Grpc.Core.Method`2",true).MakeGenericType(typeof(byte[]),typeof(byte[]));
        nativeMethod=Activator.CreateInstance(methodType,new object[]{Enum.Parse(grpc.GetType("Grpc.Core.MethodType",true),"Unary"),"SensorsRPC","GetAllSensors",marshaller,marshaller});
        nativeOptionsType=grpc.GetType("Grpc.Core.CallOptions",true);
        nativeDeadline=nativeOptionsType.GetMethod("WithDeadline",new[]{typeof(DateTime)});
        nativeRead=channelType.GetMethods().First(m=>m.Name=="BlockingUnaryCall"&&m.IsGenericMethod).MakeGenericMethod(typeof(byte[]),typeof(byte[]));
    }
    static string ReadNativePipe() {
        try {
            OpenNativePipe();
            if(nativeChannel==null)return "{\"fans\":[]}";
            object options=nativeDeadline.Invoke(Activator.CreateInstance(nativeOptionsType),new object[]{DateTime.UtcNow.AddSeconds(2)});
            byte[] reply=(byte[])nativeRead.Invoke(nativeChannel,new object[]{nativeMethod,null,options,new byte[0]});
            if(reply==null||reply.Length>1024*1024)return "{\"fans\":[]}";
            var configured=ReadConfiguredFans();
            var fans=new List<Data>();
            int position=0;
            while(position<reply.Length){
                uint tag=Varint(reply,ref position,reply.Length);
                if(tag!=10){Skip(reply,ref position,reply.Length,(int)(tag&7));continue;}
                int count=checked((int)Varint(reply,ref position,reply.Length));
                int end=checked(position+count);
                if(end>reply.Length)throw new InvalidDataException();
                string id=null;int type=0;double? value=null;
                while(position<end){
                    uint field=Varint(reply,ref position,end);
                    int wire=(int)(field&7),number=(int)(field>>3);
                    if(number==1&&wire==2){
                        int size=checked((int)Varint(reply,ref position,end));
                        if(size>end-position)throw new InvalidDataException();
                        id=Encoding.UTF8.GetString(reply,position,size);position+=size;
                    }else if(number==2&&wire==0)type=(int)Varint(reply,ref position,end);
                    else if(number==5&&wire==5){
                        if(end-position<4)throw new InvalidDataException();
                        value=BitConverter.ToSingle(reply,position);position+=4;
                    }else Skip(reply,ref position,end,wire);
                }
                string name;
                if(type==2&&value.HasValue&&!Double.IsNaN(value.Value)&&!Double.IsInfinity(value.Value)&&value.Value>=0&&id!=null&&configured.TryGetValue(id,out name))
                    fans.Add(Data.Of("name",name,"value",Math.Round(value.Value),"units","RPM"));
            }
            return Data.Of("fans",fans.ToArray()).Json();
        }catch{return "{\"fans\":[]}";}
    }
    static uint Varint(byte[] data,ref int position,int end) {
        uint result=0;
        for(int shift=0;shift<35&&position<end;shift+=7){byte b=data[position++];result|=(uint)(b&127)<<shift;if((b&128)==0)return result;}
        throw new InvalidDataException();
    }
    static void Skip(byte[] data,ref int position,int end,int wire) {
        if(wire==0){Varint(data,ref position,end);return;}
        int size=wire==1?8:wire==5?4:wire==2?checked((int)Varint(data,ref position,end)):-1;
        if(size<0||size>end-position)throw new InvalidDataException();
        position+=size;
    }
}
