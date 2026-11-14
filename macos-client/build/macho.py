"""Read Mach-O deployment commands without changing binary compatibility metadata."""
import struct
from pathlib import Path
ARCH={0x1000007:'x64',0x100000c:'arm64'}
def images(data):
 if len(data)<32:return []
 magic=struct.unpack_from('>I',data)[0]
 if magic in (0xcafebabe,0xcafebabf):
  count=struct.unpack_from('>I',data,4)[0];fmt='>iiIII' if magic==0xcafebabe else '>iiQQII';size=struct.calcsize(fmt);result=[]
  for index in range(count):
   item=struct.unpack_from(fmt,data,8+index*size);result+=images(data[item[2]:item[2]+item[3]])
  return result
 if magic not in (0xcffaedfe,0xcefaedfe):return []
 m,cpu,_,_,count,_,_=struct.unpack_from('<7I',data);offset=32 if m==0xfeedfacf else 28;minimum=None;dependencies=[]
 for _ in range(count):
  cmd,size=struct.unpack_from('<II',data,offset)
  if size<8 or offset+size>len(data):raise ValueError('Malformed Mach-O')
  if cmd in (0x32,0x24):
   version=struct.unpack_from('<I',data,offset+(12 if cmd==0x32 else 8))[0];minimum=(version>>16,(version>>8)&255,version&255)
  if cmd in (0xc,0x80000018,0x8000001f):
   name=struct.unpack_from('<I',data,offset+8)[0];dependencies.append(data[offset+name:offset+size].split(b'\0')[0].decode())
  offset+=size
 return [{'arch':ARCH.get(cpu,str(cpu)),'minimum':minimum,'dependencies':dependencies}]
def audit(root,arch,minimum):
 result=[]
 for file in sorted(Path(root).rglob('*')):
  if not file.is_file() or file.is_symlink():continue
  data=file.read_bytes();headers=images(data)
  if not headers:continue
  relevant=[item for item in headers if item['arch']==arch]
  if not relevant:raise AssertionError('Wrong native architecture: '+str(file))
  for item in relevant:
   if item['minimum'] is None or item['minimum']>minimum:raise AssertionError(f'{file}: requires {item["minimum"]}, expected <= {minimum}')
   result.append({'path':file.relative_to(root).as_posix(),**item})
 required={'MoskomVPN','libAvaloniaNative.dylib','libHarfBuzzSharp.dylib','libSkiaSharp.dylib','libe_sqlite3.dylib','libMoskomPlatform.dylib'}
 present={Path(item['path']).name for item in result}
 if not required<=present:raise AssertionError('Missing native application components: '+str(sorted(required-present)))
 return result
if __name__=='__main__':
 import argparse,json
 p=argparse.ArgumentParser();p.add_argument('root',type=Path);p.add_argument('--arch',choices=['x64','arm64'],required=True);p.add_argument('--minimum',required=True);p.add_argument('--out',type=Path,required=True);a=p.parse_args();result=audit(a.root,a.arch,tuple(map(int,a.minimum.split('.')))+(0,));a.out.write_text(json.dumps(result,indent=2));print('PASS native deployment audit:',len(result),'images')
