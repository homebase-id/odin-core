import React, {Component, useEffect} from 'react';
import {Route} from 'react-router';
import {Layout} from './components/Layout';
import {Home} from './components/Home';

import './custom.css'
import Login from "./components/Login";
import {Container} from "react-bootstrap";
import {useAppStateStore} from "./provider/AppStateStore";
import {observer} from "mobx-react-lite";

function App() {

    const state = useAppStateStore();
    useEffect(() => {
        const init = async () => {
           await state.initialize();
        };
        
        init();
        
    }, []);

    if (state.isAuthenticated) {
        return (
            <Layout>
                <Route exact path='/' component={Home}/>
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