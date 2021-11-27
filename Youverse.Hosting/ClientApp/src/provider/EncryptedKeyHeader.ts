export class EncryptedKeyHeader {
    encryptionVersion: number = 1;
    encryptionType: number = 1;
    iv: any;
    data: any;
}