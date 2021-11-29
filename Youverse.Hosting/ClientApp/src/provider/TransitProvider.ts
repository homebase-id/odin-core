import {ProviderBase} from "./ProviderBase";
import {useAppStateStore} from "./AppStateStore";
import {createEncryptionProvider} from "./EncryptionProvider";
import {RecipientList} from "./RecipientList";
import {isArray} from "util";
import {MetaData} from "./MetaData";

export class TransitProvider extends ProviderBase {

    constructor() {
        super();
    }

    async sendPayload(recipients: string | string[], message: string, file: any): Promise<string> {

        const ep = createEncryptionProvider();

        //NOTE: the order of items in this package is required
        const multipartPackage = new FormData();

        let keyHeader = ep.generateKeyHeader();

        let transferInitializationVector = ep.generateRandom16Bytes();
        let transferEncryptedKeyHeader = await ep.encryptKeyHeader(keyHeader.toJson(), transferInitializationVector);
        multipartPackage.append('tekh', transferEncryptedKeyHeader.toJson());

        let recipientList = new RecipientList();
        recipientList.Recipients = Array.isArray(recipients) ? recipients : [recipients];
        let recipientCipher = await ep.encryptAesUsingAppSharedSecret(JSON.stringify(recipientList), transferInitializationVector);

        console.log('recipientCipher', recipientCipher);
        let recipientBlob = new Blob([recipientCipher], {type: 'application/json'});

        console.log('recipientBlob', recipientBlob);

        multipartPackage.append('recipients', recipientBlob);

        /*
        :Encrypt file parts {metadata,payload} using __KeyHeader__ and places in __MultipartUploadPackage__
        (note, does not encrypt __KeyHeader__);
        */

        let metadata: MetaData = {
            preview: "this is some preview text..."
        };
        let metadataCipher = await ep.encryptAesUsingKeyHeader(JSON.stringify(metadata), keyHeader);
        multipartPackage.append('metadata', new Blob([metadataCipher], {type: 'application/json'}));

        //TODO: how to encrypt a file client side
        // const reader = new FileReader();
        // reader.readAsArrayBuffer()
        let payload = JSON.stringify({});

        let payloadCipher = await ep.encryptAesUsingKeyHeader(JSON.stringify(metadata), keyHeader);
        multipartPackage.append('payload', new Blob([payloadCipher], {type: 'application/json'}));
        console.log(multipartPackage);

        /*
        :client saves __MultipartUploadPackage__ contents on device as cache if needed;
        :client uploads __MultipartUploadPackage__ using multipart stream;
        */

        let client = this.createAxiosClient();
        let url = "/transit/client/SendPackage";

        const config = {
            headers: {
                'content-type': 'multipart/form-data',
            },
        };

        return client.post(url, multipartPackage, config).then(response => {
            return response.data;
        }).catch(error => {
            //TODO: Handle this - the file was not uploaded
            console.log(error);
            throw error;
        });
    }
}

export function createTransitProvider() {
    return new TransitProvider();
}