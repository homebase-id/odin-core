import {ArrayUtils} from "./ArrayUtils";

export class KeyHeader {
    initializationVector: any;
    encryptionKey: any;

    toJson(): string {

        let iv64 = ArrayUtils.toBase64(this.initializationVector);
        let key64 = ArrayUtils.toBase64(this.encryptionKey);
        
        return JSON.stringify({
            initializationVector: iv64,
            encryptionKey: key64
        });
    }
}

