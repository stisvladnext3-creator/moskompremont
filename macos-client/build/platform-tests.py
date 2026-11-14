from pathlib import Path
import os,subprocess,argparse
p=argparse.ArgumentParser();p.add_argument('work',type=Path);p.add_argument('--rid',required=True);p.add_argument('--flavor',required=True);a=p.parse_args();root=Path(__file__).resolve().parents[1];target=a.work/'platform-tests'
cmd=['dotnet','publish',str(root/'tests/PlatformTests.csproj'),'-c','Release','-r',a.rid,'--self-contained','true','-p:PublishSingleFile=false','-o',str(target)]
if a.flavor=='legacy':cmd+=['-p:TargetFramework=net8.0','-p:DefineConstants=LEGACY']
subprocess.run(cmd,check=True)
import shutil
shutil.copyfile(a.work/'MoskomVPN.app/Contents/MacOS/libMoskomPlatform.dylib',target/'libMoskomPlatform.dylib')
exe=target/'PlatformTests';env=os.environ.copy();env['MOSKOM_PLATFORM_HOME']=str(a.work/'keychain-test');subprocess.run([str(exe),'--updates'],env=env,check=True)
subprocess.run(['sudo','-n','env','MOSKOM_PLATFORM_HOME='+str(a.work/'root-proxy-test'),str(exe),'--proxy'],check=True)
