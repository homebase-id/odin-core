import {ProviderBase} from "./ProviderBase";

export class MediaProvider extends ProviderBase {

    private static instance: MediaProvider | null = null;

    public static ensureInstance() {
        this.instance = new MediaProvider();
        this.instance.initialize();
    }

    public static get Instance() {
        if (this.instance) {
            return this.instance;
        }

        throw new Error("MediaProvider not initialized")
    }

    initialize() {

    }

    async uploadImage(file: any): Promise<string> {

        console.log('upload image: ' + file);
        const data = new FormData();
        data.append('file', file);

        let client = this.createAxiosClient();
        let url = "/media/images";

        const config = {
            headers: {
                'content-type': 'multipart/form-data',
            },
        };

        return client.post(url, data, config).then(response => {
            return response.data;
        }).catch(error => {
            //TODO: Handle this - the file was not uploaded
            console.log(error);
            throw error;
        });
    }
}