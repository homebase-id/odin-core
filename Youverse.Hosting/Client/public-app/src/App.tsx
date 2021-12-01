import React, {useEffect, useState} from 'react';
import './App.css';
import 'bootstrap/dist/css/bootstrap.min.css';
import {useAppStateStore} from "./provider/AppStateStore";
import {BrowserRouter, Routes, Route} from 'react-router-dom';
import Layout from "./components/Layout";
import BlogLayout from "./components/Blog/BlogLayout";

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

    //https://reactrouter.com/docs/en/v6/getting-started/overview
    return (
        <Layout>
            <BrowserRouter>
                <Routes>
                    {/*<Route exact={true} path='/home' component={Home}/>*/}
                    <Route path='/blog' element={<BlogLayout/>}/>
                </Routes>
            </BrowserRouter>

        </Layout>
    );
}

export default App;