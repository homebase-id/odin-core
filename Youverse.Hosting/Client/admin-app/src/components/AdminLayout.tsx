import React, {useEffect, useState} from 'react';
import {Button, Col, Form, FormControl, Row} from "react-bootstrap";
import {Route, useLocation} from "react-router-dom";
import Sidebar from "./Sidebar";
import AdminNavbar from "./AdminNavbar";
import Footer from "./Footer";

import sidebarImage from "../assets/sidebar-2.jpg";

type Props = {};

function AdminLayout(props: Props) {
    const [image, setImage] = React.useState(sidebarImage);
    const [color, setColor] = React.useState("black");
    const [hasImage, setHasImage] = React.useState(true);
    // const location = useLocation();
    const mainPanel = React.useRef(null);
    const getRoutes = (routes) => {
        return routes.map((prop, key) => {
            if (prop.layout === "/admin") {
                return (
                    <Route
                        path={prop.layout + prop.path}
                        render={(props) => <prop.component {...props} />}
                        key={key}
                    />
                );
            } else {
                return null;
            }
        });
    };
    // React.useEffect(() => {
    //     document.documentElement.scrollTop = 0;
    //     document.scrollingElement.scrollTop = 0;
    //     mainPanel.current.scrollTop = 0;
    //     if (
    //         window.innerWidth < 993 &&
    //         document.documentElement.className.indexOf("nav-open") !== -1
    //     ) {
    //         document.documentElement.classList.toggle("nav-open");
    //         var element = document.getElementById("bodyClick");
    //         element.parentNode.removeChild(element);
    //     }
    // }, [location]);
    return (
        <>
            <div className="wrapper">
                
                <Sidebar color={color} image={hasImage ? image : ""}/>
                <div className="main-panel" ref={mainPanel}>
                    <AdminNavbar />
                    <div className="content">
                        <span>this is content</span>
                        {/*<Switch>{getRoutes(routes)}</Switch>*/}
                    </div>
                    <Footer />
                </div>
            </div>
        </>
    );
}

export default AdminLayout;