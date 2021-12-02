import React, {Component, useState} from 'react';
import './NavMenu.css';
import {Collapse, Container, Navbar, NavItem, Nav, Modal} from "react-bootstrap";
import {Link, NavLink} from "react-router-dom";
import {useAppStateStore} from "../provider/AppStateStore";

function Sidebar(props: any) {
    // const location = useLocation();
    // const activeRoute = (routeName:string) => {
    //     return location.pathname.indexOf(routeName) > -1 ? "active" : "";
    // };
    return (
        <div className="sidebar" data-image={props.image} data-color={props.color}>
            <div
                className="sidebar-background"
                style={{
                    backgroundImage: "url(" + props.image + ")",
                }}
            />
            <div className="sidebar-wrapper">
                <div className="logo d-flex align-items-center justify-content-start">
                    <a
                        href="https://www.creative-tim.com?ref=lbd-sidebar"
                        className="simple-text logo-mini mx-1">
                        <div className="logo-img">
                            {/*<img*/}
                            {/*    src={require("assets/img/reactlogo.png").default}*/}
                            {/*    alt="..."*/}
                            {/*/>*/}
                        </div>
                    </a>
                    <a className="simple-text" href="http://www.creative-tim.com">
                        Creative Tim
                    </a>
                </div>
                <Nav>
                    <NavLink
                        to={"/"}
                        className="nav-link"
                        activeClassName="active">
                        {/*<i className={prop.icon}/>*/}
                        <p>Dashboard</p>
                    </NavLink>
                    <NavLink
                        to={"/"}
                        className="nav-link"
                        activeClassName="active">
                        {/*<i className={prop.icon}/>*/}
                        <p>Community</p>
                    </NavLink>
                </Nav>
            </div>
        </div>
    );
}

export default Sidebar;

