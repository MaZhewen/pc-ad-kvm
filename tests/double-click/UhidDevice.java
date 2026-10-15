/** Test hardware boundary: record report bytes instead of writing /dev/uhid. */
public class UhidDevice {
 int reports;
 byte[] lastReport;
 public UhidDevice(String name,int[] descriptor) { }
 public UhidDevice(String name,int[] descriptor,int product) { }
 public void sendReport(byte[] report) { reports++; lastReport=report.clone(); }
 public void sendMouse(byte buttons,byte dx,byte dy,byte wheel) { }
 public void sendKeyboard(byte modifiers,byte[] keys) { }
 public void close() { }
}
