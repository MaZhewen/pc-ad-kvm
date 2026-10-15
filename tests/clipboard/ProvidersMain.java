import java.net.URI;
import java.lang.reflect.*;
public class ProvidersMain {
 public interface Ops{String read(URI uri)throws Exception;}
 public static class Client implements Ops{final String name;Client(String n){name=n;}public String read(URI uri){return name;}}
 public static void main(String[] args)throws Exception{
  final int[] leases={0},closed={0};
  AndroidProviders.Router router=new AndroidProviders.Router(authority->{leases[0]++;return new AndroidProviders.Lease(){
   public Object client(){return new Client(authority);}public void close(){closed[0]++;}
  };});
  Ops ops=(Ops)Proxy.newProxyInstance(Ops.class.getClassLoader(),new Class<?>[]{Ops.class},router);
  if(!ops.read(new URI("content://media/test")).equals("media"))throw new AssertionError("routing");
  ops.read(new URI("content://media/other"));ops.read(new URI("content://files/other"));
  if(leases[0]!=2)throw new AssertionError("lease reuse");router.close();
  if(closed[0]!=2)throw new AssertionError("cleanup");
  try{ops.read(new URI("content://media/test"));throw new AssertionError("closed accessed");}catch(IllegalStateException expected){}
  System.out.println("PASS Android content provider routing/reuse/cleanup");
 }
}
