/** Query fresh logical display information; do not retain a cached Display snapshot. */
public final class DeviceGeometry {
    public static byte[] capture()throws Exception {
        Class<?> manager=Class.forName("android.hardware.display.DisplayManagerGlobal");
        Object instance=manager.getMethod("getInstance").invoke(null);
        Object info=manager.getMethod("getDisplayInfo",int.class).invoke(instance,0);
        if(info==null)throw new IllegalStateException("Default display unavailable");
        Class<?> type=info.getClass();
        return encode(type.getField("logicalWidth").getInt(info),type.getField("logicalHeight").getInt(info),type.getField("rotation").getInt(info));
    }
    static byte[] encode(int width,int height,int rotation) {
        if(width<1||width>65535||height<1||height>65535||rotation<0||rotation>3)throw new IllegalArgumentException("Invalid display geometry");
        return new byte[]{0x10,(byte)width,(byte)(width>>>8),(byte)height,(byte)(height>>>8),(byte)rotation};
    }
}
