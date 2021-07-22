import React from "react";
import {createAuthenticationProvider} from "./AuthenticationProvider";
import {action, configure, makeObservable, observable} from "mobx";

//https://wix.github.io/react-native-navigation/docs/third-party-mobx
configure({enforceActions: "always"});

class AppStateStore {

    constructor() {
        makeObservable(this,
            {
                isAuthenticated: observable,
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
    theme: string = "light";

    async initialize(): Promise<void> {

        let tokenIsValid = await this.checkTokenStatus();

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
            this.isAuthenticated = success;
            return success;
        });

    }

    public async logout(): Promise<boolean> {
        let client = createAuthenticationProvider();
        return client.logout().then(success => {
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

    private async checkTokenStatus(): Promise<boolean> {
        const client = createAuthenticationProvider();
        return client.hasValidToken().then(result => {
            return result;
        })
    }
}

const store = new AppStateStore();
export const AppStateStoreContext = React.createContext(store);
export const useAppStateStore = () => React.useContext(AppStateStoreContext);