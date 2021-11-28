export class ArrayUtils {
    public static toArray(data: string): Uint8Array {
        const buf = new ArrayBuffer(data.length);
        const arr = new Uint8Array(buf);
        for (let i = 0, length = data.length; i < length; i++) {
            arr[i] = data.charCodeAt(i);
        }
        return arr;
    }
}