import React, { useEffect, useState } from 'react';
import './App.css';
import 'bootstrap/dist/css/bootstrap.min.css';
import { useAppStateStore } from "./provider/AppStateStore";
import { BrowserRouter, Routes, Route, Outlet } from 'react-router-dom';
import BlogLayout from "./components/Blog/BlogLayout";
import NotFoundLayout from "./components/NotFound/NotFoundLayout";
import {Container} from "react-bootstrap";
import GlobalNav from "./components/GlobalNav";
import HomeLayout from "./components/Home/HomeLayout";
import {observer} from 'mobx-react-lite';

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
        <BrowserRouter>
            <Routes>
                <Route path='/home' element={<Layout />}>
                    <Route index={true} element={<HomeLayout />} />
                    {state.isAuthenticated &&
                        <Route path='blog' element={<BlogLayout />}>
                            {/* todo: add other blog routes */}
                        </Route>
                    }
                    <Route path="/home/*" element={<NotFoundLayout />} />
                </Route>
                <Route path="*" element={<NotFoundLayout />} />
            </Routes>
        </BrowserRouter>
    );
}

function Layout(props: any) {
    return (
        <Container fluid>
            <GlobalNav/>
            <Container as="main">
                <Outlet />
            </Container>
        </Container>
    );
}

export default observer(App);