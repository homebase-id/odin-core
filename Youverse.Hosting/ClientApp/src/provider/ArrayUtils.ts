export class ArrayUtils {
    public static toArray(data: string): Uint8Array {
        const buf = new ArrayBuffer(data.length);
        const arr = new Uint8Array(buf);
        for (let i = 0, length = data.length; i < length; i++) {
            arr[i] = data.charCodeAt(i);
        }
        return arr;
    }

    public static toBase64(buffer: Uint8Array): string {
        // @ts-ignore: buffer should be number[] 
        return window.btoa(String.fromCharCode.apply(null, buffer));
    }


    //source: https://www.jocys.com/common/jsclasses/documents/Default.aspx?File=System.debug.js&Item=System.Buffer.BlockCopy&Index=0
    public static blockCopy(src: Uint8Array, srcOffset: number, dst: Uint8Array, dstOffset: number, count: number) {
        /// <summary>
        /// Copies a specified number of bytes from a source array starting at a particular
        /// offset to a destination array starting at a particular offset.
        /// </summary>
        /// <param name="src">The source buffer.</param>
        /// <param name="srcOffset">The byte offset into src.</param>
        /// <param name="dst">The destination buffer.</param>
        /// <param name="dstOffset">The byte offset into dst.</param>
        /// <param name="count">The number of bytes to copy.</param>
        for (var i = 0; i < count; i++) {
            dst[dstOffset + i] = src[srcOffset + i];
        }
    };

    static combine(first: Uint8Array, second: Uint8Array) {
        let combined = new Uint8Array(first.length+second.length)
        // Buffer.BlockCopy(first, 0, ret, 0, first.Length);
        ArrayUtils.blockCopy(first, 0, combined, 0, first.length);
        // Buffer.BlockCopy(second, 0, ret, first.Length, second.Length);
        ArrayUtils.blockCopy(second, 0, combined, first.length, second.length);
        return combined;
    }
}