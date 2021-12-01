import React from 'react';
import {Container} from "react-bootstrap";
import GlobalNav from "./GlobalNav";

function Layout(props: any) {
    return (
        <>
            <GlobalNav/>
            <Container>
                <h1>welcome!</h1>
            </Container>
            <main className="container">
                {props.children}
            </main>
        </>
    );
}

export default Layout;