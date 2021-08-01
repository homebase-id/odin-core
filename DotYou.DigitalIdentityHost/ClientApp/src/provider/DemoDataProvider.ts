import {ProviderBase} from "./ProviderBase";

class DemoDataProvider extends ProviderBase {

    // constructor() {
    //     super(null, true);
    // }

    async setPublicProfile(): Promise<void> {

        let client = this.createAxiosClient();
        return client.get("/demodata/profiledata");
    }
    
    async addContacts(): Promise<void> {
        let client = this.createAxiosClient();
        return client.get("/demodata/contacts");
    }
}

export function createDemoDataProvider() {
    return new DemoDataProvider();
}