import axios, {AxiosError} from "axios";

export class ProviderBase {
    //protected token: AuthenticationResult;
    
    //Returns the endpoint for the identity
    protected getEndpoint(): string {
        return "https://" + window.location.hostname+ "/api";
    }

    //Gets an Axios client configured with token info
    protected createAxiosClient() {
        return axios.create({
            baseURL: this.getEndpoint(),
        });
    }

    protected handleErrorResponse(error: AxiosError) {
        throw error;
    }
}