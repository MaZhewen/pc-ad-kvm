package android.graphics;
import android.view.TestState;
public class Path {
 public Path(){}
 public void moveTo(float x,float y){TestState.call("hotspot:"+x+","+y);}
 public void lineTo(float x,float y){}
 public void close(){}
}
