import React, {Component, useEffect, useState} from 'react';
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

export default observer(App);