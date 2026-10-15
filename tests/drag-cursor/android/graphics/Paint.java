package android.graphics;
public class Paint {
 public enum Style{FILL,STROKE}
 public int color; public Style style; public float strokeWidth;
 public Paint(int flags){}
 public void setColor(int color){this.color=color;}
 public void setStyle(Style style){this.style=style;}
 public void setStrokeWidth(float width){strokeWidth=width;}
}
