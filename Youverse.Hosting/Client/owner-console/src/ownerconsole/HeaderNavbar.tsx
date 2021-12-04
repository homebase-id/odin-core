import React, { Component } from "react";
import { useLocation } from "react-router-dom";
import { Navbar, Container, Nav, Dropdown, Button } from "react-bootstrap";

function HeaderNavbar() {
  // const location = useLocation();
  const mobileSidebarToggle = (e:any) => {
    // e.preventDefault();
    // document.documentElement.classList.toggle("nav-open");
    // var node = document.createElement("div");
    // node.id = "bodyClick";
    // node.onclick = function () {
    //   this.parentElement.removeChild(this);
    //   document.documentElement.classList.toggle("nav-open");
    // };
    // document.body.appendChild(node);
  };

  const getHeaderText = () => {
    return "Dashboard";
  };

  return (
    <header className="p-3 bg-dark text-white">
      <div className="container">
        <div className="d-flex flex-wrap align-items-center justify-content-center justify-content-lg-start">
          <a href="/" className="d-flex align-items-center mb-2 mb-lg-0 text-white text-decoration-none">
            {/* <svg className="bi me-2" width="40" height="32" role="img" aria-label="Bootstrap"><use xlink:href="#bootstrap"></use></svg> */}
          </a>

          <ul className="nav col-12 col-lg-auto me-lg-auto mb-2 justify-content-center mb-md-0">
            <li><a href="#" className="nav-link px-2 text-secondary">Home</a></li>
            <li><a href="#" className="nav-link px-2 text-white">Features</a></li>
            <li><a href="#" className="nav-link px-2 text-white">Pricing</a></li>
            <li><a href="#" className="nav-link px-2 text-white">FAQs</a></li>
            <li><a href="#" className="nav-link px-2 text-white">About</a></li>
          </ul>

          <form className="col-12 col-lg-auto mb-3 mb-lg-0 me-lg-3">
            <input type="search" className="form-control form-control-dark" placeholder="Search..." aria-label="Search" />
          </form>

          <div className="text-end">
            <button type="button" className="btn btn-outline-light me-2">Login</button>
            <button type="button" className="btn btn-warning">Sign-up</button>
          </div>
        </div>
      </div>
    </header>
  );
}

export default HeaderNavbar;
