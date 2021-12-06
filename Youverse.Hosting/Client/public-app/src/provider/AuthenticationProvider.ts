import {ProviderBase} from "./ProviderBase";

class AuthenticationProvider extends ProviderBase {

    //checks if the authentication token (stored in a cookie) is valid
    async hasValidToken(): Promise<boolean> {
        var value = sessionStorage.getItem("auth");
        return value === "true";
    }

    async authenticate(password: string): Promise<boolean> {
        var goodPwd = (password === "a");
        sessionStorage.setItem("auth", "true");
        return goodPwd;
    }

    async logout(): Promise<boolean> {
        sessionStorage.setItem("auth", "");
        return true;
    }
}

export function createAuthenticationProvider() {
    return new AuthenticationProvider();
}