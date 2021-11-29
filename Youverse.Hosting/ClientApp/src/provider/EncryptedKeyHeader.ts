import {ArrayUtils} from "./ArrayUtils";

export class EncryptedKeyHeader {
    encryptionVersion: number = 1;
    encryptionType: "AES" | "RSA" = "AES";
    iv: Uint8Array = new Uint8Array();
    data: Uint8Array = new Uint8Array();

    toJson(): string {
        return JSON.stringify({
            encryptionVersion: this.encryptionVersion,
            encryptionType: this.encryptionType,
            iv: ArrayUtils.toBase64(this.iv),
            data: ArrayUtils.toBase64(this.data)
        });
    };
}