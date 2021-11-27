import {ProviderBase} from "./ProviderBase";
import {useAppStateStore} from "./AppStateStore";
import {createEncryptionProvider} from "./EncryptionProvider";
import {RecipientList} from "./RecipientList";
import {isArray} from "util";

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
        let transferEncryptedKeyHeader = ep.encryptAesWithAppSharedSecret(JSON.stringify(keyHeader), transferInitializationVector);
        multipartPackage.append('tekh', transferEncryptedKeyHeader);
        
        let recipientList = new RecipientList();
        recipientList.Recipients = Array.isArray(recipients) ? recipients : [recipients];
        let recipientCipher = ep.encryptAesWithAppSharedSecret(JSON.stringify(recipientList), transferInitializationVector);
        multipartPackage.append('recipients', recipientCipher);

        /*
        :Encrypt RecipientList using __KeyHeader__ and places in __MultipartUploadPackage__;
        :Encrypt file parts {metadata,payload} using __KeyHeader__ and places in __MultipartUploadPackage__
        (note, does not encrypt __KeyHeader__);
        */


        /*
        This is the __TransferEncryptedKeyHeader__
        Client place    s __TransferEncryptedKeyHeader__ in the  __MultipartUploadPackage__;
        :client saves __MultipartUploadPackage__ contents on device as cache if needed;
        :client uploads __MultipartUploadPackage__ using multipart stream;
        */

        let metadata = "";
        let payload = "";

        multipartPackage.append('metadata', metadata);
        multipartPackage.append('payload', payload);
        multipartPackage.append('file', file);

        let client = this.createAxiosClient();
        let url = "/api/transit/client/SendPackage";

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