import {ProviderBase} from "./ProviderBase";
import {KeyHeader} from "./KeyHeader";
import {useAppStateStore} from "./AppStateStore";
import {EncryptedKeyHeader} from "./EncryptedKeyHeader";

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
    encryptAesWithAppSharedSecret(data: string, initializationVector: Uint8Array): Promise<EncryptedKeyHeader> {
        const {appSharedSecret} = useAppStateStore();

        let encryptedData = new Uint8Array([0, 1, 2]);

        let ekh: EncryptedKeyHeader = {
            encryptionType: 1,
            encryptionVersion: 1,
            iv: initializationVector,
            data: encryptedData
        }
        
        //TODO: use window.subtle.crypto
        return new Promise<EncryptedKeyHeader>(resolve => {
            return ekh;
        });
    }

    encryptAesUsingKeyHeader(data: string, keyHeader: KeyHeader): Promise<Uint8Array> {
        const {appSharedSecret} = useAppStateStore();

        //TODO: use window.subtle.crypto
        return new Promise<Uint8Array>(resolve => {
            return new Uint8Array([0, 1, 2])
        });
    }
}

export function createEncryptionProvider() {
    return new EncryptionProvider();
}
