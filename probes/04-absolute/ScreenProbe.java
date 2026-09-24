import java.io.FileOutputStream;
public class ScreenProbe {
 public static void main(String[] a) throws Exception {
  Class<?> b=Class.forName("android.graphics.Bitmap");
  Object bmp=Class.forName("android.graphics.BitmapFactory").getMethod("decodeFile",String.class).invoke(null,a[0]);
  Class<?> f=Class.forName("android.graphics.Bitmap$CompressFormat");
  FileOutputStream out=new FileOutputStream(a[1]);
  b.getMethod("compress",f,int.class,java.io.OutputStream.class).invoke(bmp,f.getField("JPEG").get(null),35,out);
  out.close();
 }
}
