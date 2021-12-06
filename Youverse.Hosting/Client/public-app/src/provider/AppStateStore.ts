import React from "react";
import { createAuthenticationProvider } from "./AuthenticationProvider";
import { action, configure, makeObservable, observable } from "mobx";

//https://wix.github.io/react-native-navigation/docs/third-party-mobx
configure({ enforceActions: "observed" });

class AppStateStore {

    constructor() {
        makeObservable(this,
            {
                isAuthenticated: observable,
                deviceToken: observable,
                theme: observable,
                initialize: action.bound,
                authenticate: action.bound,
                logout: action.bound,
                setDarkMode: action,
                setLightMode: action
            });
    }

    private isInitialized: boolean = false;
    isAuthenticated: boolean = false;
    deviceToken: string | null = null;
    theme: string = "light";

    async initialize(): Promise<void> {

        let tokenIsValid = await AppStateStore.checkTokenStatus();
        //console.log('tokenIsValid', tokenIsValid);

        if (tokenIsValid) {
            this.isAuthenticated = true;
        }

        //TODO: need to get profile data such as given name, picture, and other 
        // settings from the DI server; update drawer contents
        this.isInitialized = true;
    }

    async authenticate(password: string): Promise<boolean> {
        let client = createAuthenticationProvider();
        return client.authenticate(password).then(success => {
            console.log('setting')
            this.isAuthenticated = success;
            console.log('isAuthenticated', this.isAuthenticated);
            console.log('ssvalue', sessionStorage.getItem('auth'));
            return success;
        });
    }

    public async logout(): Promise<boolean> {
        let client = createAuthenticationProvider();
        return client.logout().then(success => {
            if (success) {
                this.isAuthenticated = false;
            }
            return success;
        });
    }

    setDarkMode() {
        this.theme = "night";
    }

    setLightMode() {
        this.theme = "light";
    }

    isNightMode(): boolean {
        return this.theme == "night";
    }

    private static async checkTokenStatus(): Promise<boolean> {
        const client = createAuthenticationProvider();
        return client.hasValidToken().then(result => {
            return result;
        })
    }
}

const store = new AppStateStore();
export const AppStateStoreContext = React.createContext(store);
export const useAppStateStore = () => React.useContext(AppStateStoreContext);