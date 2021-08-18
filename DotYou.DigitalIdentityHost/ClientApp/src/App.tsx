import React, {useEffect, useState} from 'react';
import {Route} from 'react-router-dom';
import Layout from './components/Layout';
import Home from './components/Home';

import {Spinner} from 'react-bootstrap';
import './custom.css'
import Login from "./components/Login";
import {Container} from "react-bootstrap";
import {useAppStateStore} from "./provider/AppStateStore";
import {observer} from "mobx-react-lite";
import Profile from "./components/Profile";
import PrivacySettings from "./components/PrivacySettings";
import AppLogin from "./components/AppLogin";

function App() {

    const [isInitializing, setIsInitializing] = useState(true);

    const state = useAppStateStore();
    useEffect(() => {
        const init = async () => {
            await state.initialize();
            setIsInitializing(false);
        };

        init();

    }, []);

    if (isInitializing) {
        return <Container className="h-100 align-content-center text-center">
            <Spinner
                as="span"
                animation="border"
                size="sm"
                role="status"
                aria-hidden="true"
            />
        </Container>
    }

    //HACK: if we're asking to authenticate a device
    if (window.location.href.toLowerCase().includes("?dl777")) {

        if (state.isAuthenticated && state.deviceToken) {
            //redirect to the app
            //todo: this is duplicate code
            console.log('device is authenticated; redirecting')
            window.location.href = "dotyou://auth/" + state.deviceToken;
            return;
        }
        
        return (
            <Container className="h-100 align-content-center text-center">
                <AppLogin/>;
            </Container>
        );
    }

    if (state.isAuthenticated) {
        return (
            <Layout>
                <Route exact path='/' component={Home}/>
                <Route exact path='/profile' component={Profile}/>
                <Route exact path='/privacy' component={PrivacySettings}/>
            </Layout>
        );
    }

    return (
        <Container className="h-100 align-content-center text-center">
            <Login/>
        </Container>
    );
}

// @ts-ignore
export default observer(App);