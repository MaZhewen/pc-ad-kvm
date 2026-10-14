package android.graphics;
import android.view.TestState;
public class Canvas {
 public void drawColor(int color,PorterDuff.Mode mode){if(color!=0||mode!=PorterDuff.Mode.CLEAR)throw new AssertionError("transparent canvas");TestState.call("canvas.clear");}
 public void drawPath(Path path,Paint paint){TestState.call("canvas.arrow");TestState.call("paint:"+paint.color+":"+paint.style+":"+paint.strokeWidth);}
 public void drawBitmap(Bitmap bitmap,Rect src,Rect dst,Paint paint){TestState.call("canvas.system-bitmap:"+bitmap.getWidth()+","+bitmap.getHeight());}
}
