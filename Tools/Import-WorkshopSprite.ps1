param([Parameter(Mandatory)][string]$Source,[Parameter(Mandatory)][string]$Destination,[int]$Width,[int]$Height)
# Import generated RGBA art at Silverpine's 32 pixels per world unit. Trim
# export padding, fit without stretching and area-average in premultiplied alpha.
Add-Type -AssemblyName System.Drawing
$ErrorActionPreference = 'Stop'
$refs = @([Drawing.Bitmap].Assembly.Location,[Drawing.Color].Assembly.Location,[System.Runtime.InteropServices.Marshal].Assembly.Location,[System.ComponentModel.TypeConverter].Assembly.Location,[System.Object].Assembly.Location,[Console].Assembly.Location)
$refs += @(Get-ChildItem -LiteralPath $PSHOME -Filter 'System.Private.Windows.*.dll' | ForEach-Object FullName)
Add-Type -ReferencedAssemblies $refs -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
public static class WorkshopImport {
 public static void Run(string src,string dest,int w,int h) {
  using(var b=new Bitmap(src)) using(var img=new Bitmap(w,h,PixelFormat.Format32bppArgb)) {
   var d=b.LockBits(new Rectangle(0,0,b.Width,b.Height),ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);
   int stride=d.Stride; byte[] p=new byte[stride*b.Height]; Marshal.Copy(d.Scan0,p,0,p.Length); b.UnlockBits(d);
   int l=b.Width,r=-1,t=b.Height,bot=-1;
   for(int y=0;y<b.Height;y++) for(int x=0;x<b.Width;x++) if(p[y*stride+x*4+3]>=64){l=Math.Min(l,x);r=Math.Max(r,x);t=Math.Min(t,y);bot=Math.Max(bot,y);}
   if(r<l) throw new Exception("No visible pixels");
   double s=Math.Min((double)w/(r-l+1),(double)h/(bot-t+1)); int dw=Math.Max(1,(int)Math.Round((r-l+1)*s)), dh=Math.Max(1,(int)Math.Round((bot-t+1)*s));
   for(int y=0;y<dh;y++) for(int x=0;x<dw;x++){
    int x0=l+x*(r-l+1)/dw,x1=l+(x+1)*(r-l+1)/dw,y0=t+y*(bot-t+1)/dh,y1=t+(y+1)*(bot-t+1)/dh;
    long a=0,red=0,green=0,blue=0;
    for(int j=y0;j<y1;j++) for(int i=x0;i<x1;i++){int ix=j*stride+i*4,alpha=p[ix+3];a+=alpha;blue+=alpha*p[ix];green+=alpha*p[ix+1];red+=alpha*p[ix+2];}
    if(a>0) img.SetPixel((w-dw)/2+x,h-dh+y,Color.FromArgb((int)(a/((x1-x0)*(y1-y0))),(int)(red/a),(int)(green/a),(int)(blue/a)));
   }
   img.Save(dest,ImageFormat.Png);
   Console.WriteLine("{0}x{1} art box {2}x{3}, placed {4}x{5} in {6}x{7}",b.Width,b.Height,r-l+1,bot-t+1,dw,dh,w,h);
  }
 }
}
'@
[WorkshopImport]::Run((Resolve-Path -LiteralPath $Source).Path,$ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Destination),$Width,$Height)
