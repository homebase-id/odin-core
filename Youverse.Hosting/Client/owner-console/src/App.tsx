import React from 'react';
import {BrowserRouter, Route, Routes} from 'react-router-dom';
import 'bootstrap/dist/css/bootstrap.min.css';
import "@fortawesome/fontawesome-free/css/all.min.css";
import "./assets/App.css";
import {observer} from "mobx-react-lite";
import OwnerConsoleLayout from './ownerconsole/OwnerConsoleLayout';
import AppLogin from "./appLogin/AppLogin";

function App() {
    return (
        <BrowserRouter>
            <Routes>
                <Route path='/owner/*' element={<OwnerConsoleLayout/>}/>
                <Route path='/applogin/*' element={<AppLogin/>}/>
            </Routes>
        </BrowserRouter>);
}

// @ts-ignore
export default observer(App);