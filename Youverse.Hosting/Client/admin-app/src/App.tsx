import React from 'react';
import {BrowserRouter, Route, Routes} from 'react-router-dom';
import 'bootstrap/dist/css/bootstrap.min.css';
import "@fortawesome/fontawesome-free/css/all.min.css";
import "./assets/App.css";
import {observer} from "mobx-react-lite";
import Layout from './components/Layout';
import AppLogin from "./components/AppLogin";

function App() {
    return (
        <BrowserRouter>
            <Routes>
                <Route path='/admin/*' element={<Layout/>}/>
                <Route path='/applogin' element={<AppLogin/>}/>
            </Routes>
        </BrowserRouter>);
}

// @ts-ignore
export default observer(App);