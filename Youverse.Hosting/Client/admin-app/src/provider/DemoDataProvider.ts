import {ProviderBase} from "./ProviderBase";

class DemoDataProvider extends ProviderBase {

    // constructor() {
    //     super(null, true);
    // }

    async setPublicProfile(): Promise<void> {

        let client = this.createAxiosClient();
        return client.get("/demodata/profiledata").then(response => {
            if (response.status !== 200) {
                throw new Error("failed to set public profile");
            }
        });
    }

    async addConnectionRequests(): Promise<void> {
        let client = this.createAxiosClient();
        return client.get("/demodata/connectionrequest").then(response => {
            if (response.status !== 200) {
                throw new Error("failed to set send connection requests");
            }
        })
    }
}

export function createDemoDataProvider() {
    return new DemoDataProvider();
}