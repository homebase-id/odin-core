import {ProviderBase} from "./ProviderBase";

class AuthenticationProvider extends ProviderBase {

    //checks if the authentication token (stored in a cookie) is valid
    async hasValidToken(): Promise<boolean> {
        return true;
    }

    async authenticate(password: string): Promise<boolean> {
        return (password === "a");
    }

    async logout(): Promise<boolean> {
        return true;
    }
}

export function createAuthenticationProvider() {
    return new AuthenticationProvider();
}