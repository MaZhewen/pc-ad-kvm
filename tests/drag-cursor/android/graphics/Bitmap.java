package android.graphics;
public class Bitmap {
 private final boolean resource;
 public Bitmap(){this(false);} public Bitmap(boolean resource){this.resource=resource;}
 public int getWidth(){return 60;} public int getHeight(){return 60;}
 public void getPixels(int[] pixels,int offset,int stride,int x,int y,int width,int height) {
  if(resource || !android.view.TestState.transparentIcon)pixels[offset]=0xffffffff;
 }
}
