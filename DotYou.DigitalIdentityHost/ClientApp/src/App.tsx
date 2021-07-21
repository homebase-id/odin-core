import React, {Component} from 'react';
import {Route} from 'react-router';
import {Layout} from './components/Layout';
import {Home} from './components/Home';

import './custom.css'
import Login from "./components/Login";
import {Container} from "react-bootstrap";


export default class App extends Component {
    static displayName = App.name;


    render() {
        let isAuthenticated: boolean = false;

        if (isAuthenticated) {
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
}
