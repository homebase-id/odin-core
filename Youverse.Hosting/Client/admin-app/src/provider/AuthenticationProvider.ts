import {AuthenticationPayload, AuthenticationReplyNonce, AuthenticationResult, NonceData} from "./AuthenticationTypes";
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
            });
        });
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
            });
        });
    }

    async logout(): Promise<boolean> {

        let client = this.createAxiosClient();

        //withCredentials lets us set the cookies return from the /admin/authentication endpoint
        return client.get("/admin/authentication/logout", {withCredentials: true}).then(response => {
            return response.data;
        });
    }


    private async prepareAuthPassword(password: string, nonceData: NonceData, hp: boolean = false): Promise<AuthenticationReplyNonce> {

        const interations = 100000;
        const len = 16;

        let hashedPassword64 = await this.wrapPbkdf2HmacSha256(password, nonceData.saltPassword64, interations, len);
        let hashNoncePassword64 = await this.wrapPbkdf2HmacSha256(hashedPassword64, nonceData.nonce64, interations, len);
        let hashedKek64 = await this.wrapPbkdf2HmacSha256(password, nonceData.saltKek64, interations, len);

        let base64Key = this.rsaPemStrip(nonceData.publicPem);
        let key = await this.rsaImportKey(base64Key);

        let secret = window.crypto.getRandomValues(new Uint8Array(16));
        //@ts-ignore: ignore complaint about not using a number[] for the 'secret' param
        let secret64 = btoa(String.fromCharCode.apply(null, secret));

        let payload: AuthenticationPayload =
            {
                hpwd64: hashedPassword64,
                kek64: hashedKek64,
                secret: secret64
            }

        let encryptable = JSON.stringify(payload);
        let cipher = await this.rsaOaepEncrypt(key, encryptable);

        //@ts-ignore: ignore complaint about not using a number[] for the 'cipher' param
        let cipher64 = btoa(String.fromCharCode.apply(null, cipher));
        return {
            nonce64: nonceData.nonce64,
            nonceHashedPassword64: hashNoncePassword64,
            crc: nonceData.crc,
            rsaEncrypted: cipher64
        };
    }

    private async getNonce(): Promise<NonceData> {
        let client = this.createAxiosClient();
        return client.get("/admin/authentication/nonce").then(response => {
            return response.data;
        }).catch(super.handleErrorResponse);
    }

    private async getSalts(): Promise<NonceData> {
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
                //@ts-ignore    
                let base64 = btoa(String.fromCharCode.apply(null, hashed));
                return base64;
            }
        );
    }

    private fromBase64string(data64: string): Uint8Array {
        return Uint8Array.from(atob(data64), c => c.charCodeAt(0))
    }


    private rsaPemStrip(pem: string) {
        var s = pem.replace('-----BEGIN PUBLIC KEY-----', '');
        s = s.replace('-----END PUBLIC KEY-----', '');

        return s.replace('\n', '');
    }


    // from https://developers.google.com/web/updates/2012/06/How-to-convert-ArrayBuffer-to-and-from-String
    str2ab(str: string): ArrayBuffer {
        const buf = new ArrayBuffer(str.length);
        const bufView = new Uint8Array(buf);

        for (let i = 0, strLen = str.length; i < strLen; i++) {
            bufView[i] = str.charCodeAt(i);
        }

        return buf;
    }

    // key is base64 encoded
    async rsaImportKey(key64: string): Promise<CryptoKey> {

        // base64 decode the string to get the binary data
        const binaryDerString = window.atob(key64);
        // convert from a binary string to an ArrayBuffer
        const binaryDer = this.str2ab(binaryDerString);

        return window.crypto.subtle.importKey(
            "spki",
            binaryDer,
            {
                name: "RSA-OAEP",
                //modulusLength: 256,
                hash: {name: "SHA-256"}
            },
            false,
            ["encrypt"] //must be ["encrypt", "decrypt"] or ["wrapKey", "unwrapKey"]
        );

        // console.log("Imported key = ", key);
        // return key;
    }

    async rsaOaepEncrypt(publicKey: CryptoKey, str: string) {
        return window.crypto.subtle.encrypt(
            {
                name: "RSA-OAEP",
                //label: Uint8Array([...]) //optional
            },
            publicKey, //from generateKey or importKey above
            this.str2ab(str) //ArrayBuffer of data you want to encrypt
        ).then(encrypted => {
            console.log("RSA Encrypted = ", encrypted);
            return new Uint8Array(encrypted);
        });
    }
}

export function createAuthenticationProvider() {
    return new AuthenticationProvider();
}