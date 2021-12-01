import React, {useEffect, useState} from 'react';
import './App.css';
import 'bootstrap/dist/css/bootstrap.min.css';
import Container from 'react-bootstrap/Container';
import GlobalNav from "./components/GlobalNav";
import {useAppStateStore} from "./provider/AppStateStore";
import {Route} from 'react-router-dom';
import Layout from "./components/Layout";

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
    
    return (
        <Layout>
            {/*<Route exact={true} path='/home' component={Home}/>*/}
            {/*<Route exact path='/profile' component={Profile}/>*/}
        </Layout>
    );
}

export default App;