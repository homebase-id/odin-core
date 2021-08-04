import {AuthenticationReplyNonce, AuthenticationResult, ClientNoncePackage} from "./AuthenticationTypes";
import {ProviderBase} from "./ProviderBase";

class AuthenticationProvider extends ProviderBase {

    // constructor() {
    //     super(null, false);
    // }

    //checks if the authentication token (stored in a cookie) is valid
    async hasValidToken(): Promise<boolean> {

        //Note: the token is in a cookie marked http-only so making 
        // the call to the endpoint will automatically include the 
        // cookie.  we just need to check the success code 

        let client = this.createAxiosClient();
        return client.get("/admin/authentication/verifyToken").then(response => {
            return response.data;
        });
    }

    async authenticate(password: string): Promise<boolean> {
        return this.getNonce().then(noncePackage => {

            return this.prepareAuthPassword(password, noncePackage).then(reply => {
                let client = this.createAxiosClient();

                //withCredentials lets us set the cookies return from the /admin/authentication endpoint
                return client.post("/admin/authentication", reply, {withCredentials: true}).then(response => {
                    if (response.status === 200) {
                        return response.data;
                    }

                    return false;
                }).catch(super.handleErrorResponse);
            })
        })
    }

    //returns a device token
    async authenticateDevice(password: string): Promise<string> {
        return this.getNonce().then(noncePackage => {

            return this.prepareAuthPassword(password, noncePackage).then(reply => {
                let client = this.createAxiosClient();

                return client.post("/admin/authentication/device", reply, {withCredentials: true}).then(response => {
                    if (response.status === 200) {
                        return response.data;
                    }

                    return null;
                }).catch(super.handleErrorResponse);
            })
        })
    }

    async logout(): Promise<boolean> {

        let client = this.createAxiosClient();

        //withCredentials lets us set the cookies return from the /admin/authentication endpoint
        return client.get("/admin/authentication/logout", {withCredentials: true}).then(response => {
            return response.data;
        });
    }


    private async prepareAuthPassword(password: string, noncePackage: ClientNoncePackage, hp: boolean = false): Promise<AuthenticationReplyNonce> {

        const interations = 100000;
        const len = 16;

        let hashedPassword64 = await this.wrapPbkdf2HmacSha256(password, noncePackage.saltPassword64, interations, len);
        let hashNoncePassword64 = await this.wrapPbkdf2HmacSha256(hashedPassword64, noncePackage.nonce64, interations, len);
        let hashedKek64 = await this.wrapPbkdf2HmacSha256(password, noncePackage.saltKek64, interations, len);

        return {
            nonce64: noncePackage.nonce64,
            keK64: hashedKek64,
            nonceHashedPassword64: hashNoncePassword64,
            hashedPassword64: hp ? hashedPassword64 : ""
        };
    }

    private async getNonce(): Promise<ClientNoncePackage> {
        let client = this.createAxiosClient();
        return client.get("/admin/authentication/nonce").then(response => {
            return response.data;
        }).catch(super.handleErrorResponse);
    }

    private async getSalts(): Promise<ClientNoncePackage> {
        let client = this.createAxiosClient();
        return client.get("/admin/authentication/getsalts").then(response => {
            return response.data;
        });
    }

    async forceSetPassword_temp(newPassword: string): Promise<boolean> {
        return this.getSalts().then(salts => {
            return this.prepareAuthPassword(newPassword, salts, true).then(reply => {
                return this.createAxiosClient().post("/admin/authentication/todo_move_this", reply).then(response => {
                    return response.status === 200;
                });
            });
        });
    }

    // ================== PBKDF ==================
    /**
     * @param {string} strPassword The clear text password
     * @param {Uint8Array} salt    The salt
     * @param {string} hash        The Hash model, e.g. ["SHA-256" | "SHA-512"]
     * @param {int} iterations     Number of iterations
     * @param {int} len            The output length in bytes, e.g. 16
     */
    private async pbkdf2(strPassword: string, salt: Uint8Array, hash: string, iterations: number, len: number): Promise<Uint8Array> {
        let password = new TextEncoder().encode(strPassword);

        let ik = await window.crypto.subtle.importKey("raw", password, {name: "PBKDF2"}, false, ["deriveBits"]);
        let dk = await window.crypto.subtle.deriveBits(
            {
                name: "PBKDF2",
                hash: hash,
                salt: salt,
                iterations: iterations
            },
            ik,
            len * 8);  // Bytes to bits

        return new Uint8Array(dk);
    }

    private wrapPbkdf2HmacSha256(password: string, saltArray64: string, iterations: number, len: number): Promise<string> {
        let u8salt = Uint8Array.from(atob(saltArray64), c => c.charCodeAt(0));

        return this.pbkdf2(password, u8salt, "SHA-256", iterations, len).then(hashed => {
                let base64 = btoa(String.fromCharCode.apply(null, hashed));
                return base64;
            }
        );
    }

    private fromBase64string(data64: string): Uint8Array {
        return Uint8Array.from(atob(data64), c => c.charCodeAt(0))
    }
}

export function createAuthenticationProvider() {
    return new AuthenticationProvider();
}