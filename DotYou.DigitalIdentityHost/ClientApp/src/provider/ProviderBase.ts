import axios, {AxiosError} from "axios";
import {AuthenticationResult} from "./AuthenticationTypes";

export class ProviderBase {
    //protected token: AuthenticationResult;

    // constructor(token: AuthenticationResult | null, assertValidToken: boolean) {
    //
    //     if (assertValidToken && !token) {
    //         throw new Error("Invalid token specified")
    //     }
    //
    //     //@ts-ignore: ignoring since we check above
    //     this.token = token;
    // }

    //Returns the endpoint for the identity
    protected getEndpoint(): string {
        return "https://" + window.location.hostname+ "/api";
    }

    //Gets an Axios client configured with token info
    protected createAxiosClient() {
        return axios.create({
            baseURL: this.getEndpoint(),
            // headers:
            //     {
            //         'X-DI-Admin-Auth-Token': this.token?.token ?? "",
            //         'X-DI-Admin-Auth-KEK': this.token?.token2 ?? ""
            //     }
        });
    }

    protected handleErrorResponse(error: AxiosError) {
        throw error;


    }
}