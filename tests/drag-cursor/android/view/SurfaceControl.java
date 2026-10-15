package android.view;
public class SurfaceControl {
 public void release(){TestState.call("layer.release");}
 public static class Builder {
  public Builder(){TestState.call("builder");}
  public Builder setName(String n){TestState.call("name:"+n);return this;}
  public Builder setBufferSize(int w,int h){TestState.call("size:"+w+","+h);return this;}
  public Builder setFormat(int f){TestState.call("format:"+f);return this;}
  public Builder setHidden(boolean h){TestState.call("hidden:"+h);return this;}
  public Builder setOpaque(boolean o){TestState.call("opaque:"+o);return this;}
  public Builder setParent(SurfaceControl p){if(p!=null)throw new AssertionError("parent must be null");TestState.call("parent:null");return this;}
  public SurfaceControl build(){TestState.call("build");return TestState.layer=new SurfaceControl();}
 }
 public static class Transaction {
  public Transaction(){TestState.call("transaction");}
  public Transaction setLayerStack(SurfaceControl s,int v){TestState.call("stack:"+v);return this;}
  public Transaction setLayer(SurfaceControl s,int v){TestState.call("z:"+v);return this;}
  public Transaction setTrustedOverlay(SurfaceControl s,boolean v){TestState.call("trusted:"+v);return this;}
  public Transaction setPosition(SurfaceControl s,float x,float y){TestState.call("position:"+x+","+y);return this;}
  public Transaction show(SurfaceControl s){TestState.call("show");return this;}
  public Transaction hide(SurfaceControl s){TestState.call("hide");return this;}
  public Transaction remove(SurfaceControl s){TestState.call("remove");return this;}
  public void apply(){TestState.call("apply");}
  public void close(){TestState.call("transaction.close");}
 }
}
