import {ProviderBase} from "./ProviderBase";
import {KeyHeader} from "./KeyHeader";
import {useAppStateStore} from "./AppStateStore";
import {EncryptedKeyHeader} from "./EncryptedKeyHeader";
import {ArrayUtils} from "./ArrayUtils";

export class EncryptionProvider extends ProviderBase {

    constructor() {
        super();
    }

    //generates a new KeyHeader with random values
    generateKeyHeader(): KeyHeader {
        let header = new KeyHeader();
        header.initializationVector = this.generateRandom16Bytes();
        header.encryptionKey = this.generateRandom16Bytes();
        return header;
    }

    //Generates a random 16 byte array
    generateRandom16Bytes(): Uint8Array {
        return window.crypto.getRandomValues(new Uint8Array(16));
    }

    //Encrypts the data with AES using the initializationVector and appSharedSecret from state
    encryptKeyHeader(data: string, initializationVector: Uint8Array): Promise<EncryptedKeyHeader> {
        return this.encryptAesUsingAppSharedSecret(data, initializationVector).then(encryptedData => {
            let ekh: EncryptedKeyHeader = {
                encryptionType: 1,
                encryptionVersion: 1,
                iv: initializationVector,
                data: encryptedData
            }
            return ekh;
        });
    }

    encryptAesUsingAppSharedSecret(data: string, initializationVector: Uint8Array): Promise<Uint8Array> {
        const {appSharedSecret} = useAppStateStore();
        return EncryptionProvider.aesCbcEncrypt(ArrayUtils.toArray(data), appSharedSecret, initializationVector).then(encryptedData => {
            return encryptedData;
        });
    }

    encryptAesUsingKeyHeader(data: string, keyHeader: KeyHeader): Promise<Uint8Array> {
        return EncryptionProvider.aesCbcEncrypt(ArrayUtils.toArray(data), keyHeader.encryptionKey, keyHeader.initializationVector).then(encryptedData => {
            return encryptedData;
        });
    }


    private static async aesCbcEncrypt(u8aData: Uint8Array, u8aKey: Uint8Array, iv: Uint8Array): Promise<Uint8Array> {
        let key = await crypto.subtle.importKey(
            "raw",
            u8aKey,
            {   //this is the algorithm options
                name: "AES-CBC",
            },
            false, //whether the key is extractable (i.e. can be used in exportKey)
            ["encrypt", "decrypt"] //can be "encrypt", "decrypt", "wrapKey", or "unwrapKey"
        );

        let cipher = await crypto.subtle.encrypt(
            {
                name: "AES-CBC",
                iv: iv,
            },
            key, //from generateKey or importKey above
            u8aData //ArrayBuffer of data you want to encrypt
        );

        return new Uint8Array(cipher);
    }

    private static async aesCbcDecrypt(u8aCipher: Uint8Array, u8aKey: Uint8Array, iv: Uint8Array): Promise<Uint8Array> {
        // console.log("Decrypt IV = " + iv);
        let key = await crypto.subtle.importKey(
            "raw",
            u8aKey,
            {   //this is the algorithm options
                name: "AES-CBC",
            },
            false, //whether the key is extractable (i.e. can be used in exportKey)
            ["encrypt", "decrypt"] //can be "encrypt", "decrypt", "wrapKey", or "unwrapKey"
        );

        let decrypted = await crypto.subtle.decrypt(
            {
                name: "AES-CBC",
                iv: iv, //The initialization vector you used to encrypt
            },
            key, //from generateKey or importKey above
            u8aCipher //ArrayBuffer of the data
        );

        return new Uint8Array(decrypted);
    }

}

export function createEncryptionProvider() {
    return new EncryptionProvider();
}
