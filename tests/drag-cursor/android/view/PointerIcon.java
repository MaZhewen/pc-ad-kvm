package android.view;
import android.content.Context;
import android.graphics.Bitmap;
public class PointerIcon {
 public static final int TYPE_DEFAULT=1000;
 private float mHotSpotX=11, mHotSpotY=8;
 public static PointerIcon getLoadedSystemIcon(Context context,int type,boolean large,float scale) {
  if(!TestState.systemIcon)throw new UnsupportedOperationException();
  if(type!=TYPE_DEFAULT || large || scale!=1f)throw new AssertionError("unexpected icon request");
  return new PointerIcon();
 }
 public Bitmap getBitmap(){return new Bitmap();}
}
