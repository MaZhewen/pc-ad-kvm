package android.view;
import android.graphics.Canvas;
import android.graphics.Rect;
public class Surface {
 public Surface(SurfaceControl s){TestState.call("surface");}
 public Canvas lockCanvas(Rect r){TestState.call("canvas.lock");return new Canvas();}
 public void unlockCanvasAndPost(Canvas c){TestState.call("canvas.post");}
 public void release(){TestState.call("surface.release");}
}
