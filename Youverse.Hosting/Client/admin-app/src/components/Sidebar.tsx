import React, {Component, useState} from 'react';
import {Collapse, Container, Navbar, NavItem, Nav, Modal} from "react-bootstrap";
import {Link, NavLink, useLocation} from "react-router-dom";
import {useAppStateStore} from "../provider/AppStateStore";
import routes from './Routes';

function Sidebar(props: any) {

    const [collapsed, setCollapsed] = useState<boolean>(false);
    const [showLogoutModal, setShowLogoutModal] = useState<boolean>(false);
    const {logout} = useAppStateStore();

    const toggleNavbar = () => {
        setCollapsed(!collapsed);
    }

    const handleLogout = () => {
        setShowLogoutModal(true);
        logout().then(success => {
            if (success) {
                window.location.href = "/"
            }
        });
    }

    const location = useLocation();
    const activeRoute = (routeName:string) => {
        return location.pathname.indexOf(routeName) > -1 ? "active" : "";
    };
    
    return (
    <div className="sidebar" data-image={props.image} data-color={props.color}>
            <div className="sidebar-background" style={{ backgroundImage: "url(" + props.image + ")" }} />
      <div className="sidebar-wrapper">
        <div className="logo d-flex align-items-center justify-content-start">
          <a href="/admin" className="simple-text logo-mini mx-1">
            <div className="logo-img">
              {/* <img
                src={require("assets/img/reactlogo.png").default}
                alt="..."
              /> */}
            </div>
          </a>
          <a className="simple-text" href="">
            Youverse
          </a>
        </div>
        <Nav>
          {routes.map((prop, key) => {
              return (
                <li className={activeRoute(prop.path)} key={key}>
                  <NavLink
                    to={prop.path}
                    className="nav-link">
                    <i className={prop.icon} />
                    <p>{prop.title}</p>
                  </NavLink>
                </li>
              );
            return null;
          })}
        </Nav>
      </div>
    </div>
    );
    
}

export default Sidebar;

