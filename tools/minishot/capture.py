"""Small Windows test capture using GDI; no installed imaging dependency."""
import ctypes,struct,zlib
from ctypes import wintypes as w
g=ctypes.WinDLL('gdi32',use_last_error=True);u=ctypes.WinDLL('user32',use_last_error=True)
u.GetDC.argtypes=[w.HWND];u.GetDC.restype=w.HDC;u.ReleaseDC.argtypes=[w.HWND,w.HDC]
g.CreateCompatibleDC.argtypes=[w.HDC];g.CreateCompatibleDC.restype=w.HDC
g.CreateDIBSection.argtypes=[w.HDC,ctypes.c_void_p,w.UINT,ctypes.POINTER(ctypes.c_void_p),w.HANDLE,w.DWORD];g.CreateDIBSection.restype=w.HBITMAP
g.SelectObject.argtypes=[w.HDC,w.HANDLE];g.SelectObject.restype=w.HANDLE;g.DeleteObject.argtypes=[w.HANDLE];g.DeleteDC.argtypes=[w.HDC]
g.BitBlt.argtypes=[w.HDC,ctypes.c_int,ctypes.c_int,ctypes.c_int,ctypes.c_int,w.HDC,ctypes.c_int,ctypes.c_int,w.DWORD]
class Shot:
 def __init__(self,x,y,width,height):
  self.width=width;self.height=height;assert 0<width<=4096 and 0<height<=4096
  screen=u.GetDC(None);dc=g.CreateCompatibleDC(screen);bits=ctypes.c_void_p();header=struct.pack('<IiiHHIIiiII',40,width,-height,1,32,0,width*height*4,0,0,0,0);buffer=ctypes.create_string_buffer(header)
  bitmap=g.CreateDIBSection(screen,buffer,0,ctypes.byref(bits),None,0);assert bitmap and bits.value
  previous=g.SelectObject(dc,bitmap)
  try:
   assert g.BitBlt(dc,0,0,width,height,screen,x,y,0x40cc0020),'test screen capture failed';self.bgra=ctypes.string_at(bits,width*height*4)
  finally:g.SelectObject(dc,previous);g.DeleteObject(bitmap);g.DeleteDC(dc);u.ReleaseDC(None,screen)
 def getpixel(self,point):
  x,y=point;assert 0<=x<self.width and 0<=y<self.height;at=(y*self.width+x)*4;return self.bgra[at+2],self.bgra[at+1],self.bgra[at]
 def save(self,path):
  rows=[]
  for y in range(self.height):
   row=self.bgra[y*self.width*4:(y+1)*self.width*4];rgb=bytearray(self.width*3);rgb[0::3]=row[2::4];rgb[1::3]=row[1::4];rgb[2::3]=row[0::4];rows.append(b'\0'+rgb)
  def chunk(kind,data):return struct.pack('>I',len(data))+kind+data+struct.pack('>I',zlib.crc32(kind+data)&0xffffffff)
  path.write_bytes(b'\x89PNG\r\n\x1a\n'+chunk(b'IHDR',struct.pack('>IIBBBBB',self.width,self.height,8,2,0,0,0))+chunk(b'IDAT',zlib.compress(b''.join(rows),3))+chunk(b'IEND',b''))
