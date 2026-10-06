// Reference additions for dev.comfyfluffy.caustica.ngx.NgxLibrary.
//
// These handles are OPTIONAL by design. A normal Caustica build using an older
// ngxshim.dll must continue to load and simply report DLSS-NR unavailable.

// Fields:
// private final MethodHandle dlssNrAvailable;
// private final MethodHandle createDlssNr;
// private final MethodHandle evaluateDlssNr;

// Constructor additions:
//
// this.dlssNrAvailable = optionalHandle(
//     lookup,
//     "ngxshim_dlssnr_available",
//     FunctionDescriptor.of(ValueLayout.JAVA_INT));
//
// this.createDlssNr = optionalHandle(
//     lookup,
//     "ngxshim_create_dlssnr",
//     FunctionDescriptor.of(
//         ValueLayout.ADDRESS,
//         ValueLayout.JAVA_LONG,  // VkCommandBuffer
//         ValueLayout.JAVA_INT,   // displayWidth
//         ValueLayout.JAVA_INT)); // displayHeight
//
// this.evaluateDlssNr = optionalHandle(
//     lookup,
//     "ngxshim_evaluate_dlssnr",
//     FunctionDescriptor.of(
//         ValueLayout.JAVA_INT,
//         ValueLayout.JAVA_LONG,    // cmd
//         ValueLayout.ADDRESS,      // feature
//
//         ValueLayout.JAVA_LONG, ValueLayout.JAVA_LONG, ValueLayout.JAVA_INT, // color
//         ValueLayout.JAVA_LONG, ValueLayout.JAVA_LONG, ValueLayout.JAVA_INT, // depth
//         ValueLayout.JAVA_LONG, ValueLayout.JAVA_LONG, ValueLayout.JAVA_INT, // motion
//         ValueLayout.JAVA_LONG, ValueLayout.JAVA_LONG, ValueLayout.JAVA_INT, // mask
//         ValueLayout.JAVA_LONG, ValueLayout.JAVA_LONG, ValueLayout.JAVA_INT, // output
//
//         ValueLayout.JAVA_INT, ValueLayout.JAVA_INT, // display W/H
//         ValueLayout.JAVA_INT, ValueLayout.JAVA_INT, // aux W/H
//         ValueLayout.JAVA_FLOAT, ValueLayout.JAVA_FLOAT, // MV scale
//         ValueLayout.JAVA_INT, ValueLayout.JAVA_INT, // depth inverted, reset
//
//         ValueLayout.JAVA_FLOAT, // intensity
//         ValueLayout.JAVA_FLOAT, // local tone
//         ValueLayout.JAVA_FLOAT, // local structure
//         ValueLayout.JAVA_FLOAT, // global tone
//         ValueLayout.JAVA_FLOAT, // skin structure
//         ValueLayout.JAVA_INT,   // style
//         ValueLayout.JAVA_INT)); // auto mask

public boolean hasDlssNr() {
    return dlssNrAvailable != null
            && createDlssNr != null
            && evaluateDlssNr != null;
}

public boolean dlssNrAvailable() {
    if (dlssNrAvailable == null)
        return false;

    try {
        return ((int) dlssNrAvailable.invokeExact()) != 0;
    } catch (Throwable t) {
        throw new RuntimeException(
                "ngxshim_dlssnr_available failed",
                t);
    }
}

public MemorySegment createDlssNr(
        long cmd,
        int displayWidth,
        int displayHeight) {

    if (createDlssNr == null)
        return MemorySegment.NULL;

    try {
        return (MemorySegment) createDlssNr.invokeExact(
                cmd,
                displayWidth,
                displayHeight);
    } catch (Throwable t) {
        throw new RuntimeException(
                "ngxshim_create_dlssnr failed",
                t);
    }
}

public int evaluateDlssNr(
        long cmd,
        MemorySegment feature,

        long colorView,
        long colorImage,
        int colorFormat,

        long depthView,
        long depthImage,
        int depthFormat,

        long motionView,
        long motionImage,
        int motionFormat,

        long maskView,
        long maskImage,
        int maskFormat,

        long outputView,
        long outputImage,
        int outputFormat,

        int displayWidth,
        int displayHeight,
        int auxWidth,
        int auxHeight,

        float mvScaleX,
        float mvScaleY,

        int depthInverted,
        int reset,

        float intensity,
        float localTone,
        float localStructure,
        float globalTone,
        float skinStructure,
        int style,
        int useAutoMask) {

    if (evaluateDlssNr == null)
        return -1;

    try {
        return (int) evaluateDlssNr.invokeExact(
                cmd,
                feature,

                colorView,
                colorImage,
                colorFormat,

                depthView,
                depthImage,
                depthFormat,

                motionView,
                motionImage,
                motionFormat,

                maskView,
                maskImage,
                maskFormat,

                outputView,
                outputImage,
                outputFormat,

                displayWidth,
                displayHeight,
                auxWidth,
                auxHeight,

                mvScaleX,
                mvScaleY,

                depthInverted,
                reset,

                intensity,
                localTone,
                localStructure,
                globalTone,
                skinStructure,
                style,
                useAutoMask);
    } catch (Throwable t) {
        throw new RuntimeException(
                "ngxshim_evaluate_dlssnr failed",
                t);
    }
}
