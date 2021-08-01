import React, {Component} from 'react';
import { Button, Image} from "react-bootstrap";
import {createDemoDataProvider} from "../provider/DemoDataProvider";

function Home(props: any) {

    const ddp = createDemoDataProvider();
    function addDemoData() {
        ddp.addContacts().then(()=>
        {
            ddp.setPublicProfile().then((r)=>
            {
                window.alert("Done");
            })
        });
        
    }

    return (
        <div>
            <Image src="https://images.gr-assets.com/hostedimages/1568664189ra/28157476.gif"/>
            <h1>Welcome</h1>
            
            
            <Button onClick={addDemoData}>Add Demo Data</Button>
        </div>
    );
}

export default Home;