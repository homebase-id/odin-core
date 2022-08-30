"use strict";(self.webpackChunkowner_app=self.webpackChunkowner_app||[]).push([[755],{2723:function(e,t,n){n.d(t,{Z:function(){return v}});var r=n(1413),a=n(885),i=n(1803),s=n(2791),o=n(4990),l=n(4164),c=n(3412),u=n(8191),d=n(184),m=function(e){return(0,d.jsx)("input",(0,r.Z)((0,r.Z)({},e),{},{type:"radio",className:"h-4 w-4 rounded-full border-gray-300 bg-gray-100 text-blue-600 focus:outline-none dark:border-gray-600 dark:bg-gray-700 dark:ring-offset-gray-800"}))},f=function(e){var t=e.title,n=e.confirmText,f=e.isOpen,x=e.acl,h=e.onConfirm,g=e.onCancel,p=(0,c.Z)("modal-container"),v=(0,s.useState)(x),b=(0,a.Z)(v,2),j=b[0],y=b[1];if(!f)return null;var C=function(e){y((0,r.Z)((0,r.Z)({},x),{},{requiredSecurityGroup:i.hh[e.target.value]}))},w=(0,d.jsxs)("div",{className:"relative z-50","aria-labelledby":"modal-title",role:"dialog","aria-modal":"true",children:[(0,d.jsx)("div",{className:"fixed inset-0 bg-gray-500 bg-opacity-75 transition-opacity"}),(0,d.jsx)("div",{className:"fixed inset-0 z-10 overflow-y-auto",children:(0,d.jsx)("div",{className:"flex min-h-full items-end justify-center p-4 text-center sm:items-center sm:p-0",children:(0,d.jsxs)("div",{className:"relative transform overflow-hidden rounded-lg bg-white text-left shadow-xl transition-all dark:bg-black sm:my-8 sm:w-full sm:max-w-lg",children:[(0,d.jsx)("div",{className:"bg-white px-4 pt-5 pb-4 dark:bg-black dark:text-white sm:p-6 sm:pb-4",children:(0,d.jsx)("div",{className:"sm:flex sm:items-start",children:(0,d.jsxs)("div",{className:"mt-3 text-center sm:mt-0 sm:ml-4 sm:text-left",children:[(0,d.jsx)("h3",{className:"mb-3 text-lg font-medium leading-6 text-gray-900 dark:text-slate-50",id:"modal-title",children:t}),(0,d.jsxs)("div",{children:[(0,d.jsx)(m,{id:"anonymous",value:"Anonymous",name:"securityGroup",checked:j.requiredSecurityGroup===i.hh.Anonymous,onChange:C}),(0,d.jsx)("label",{htmlFor:"anonymous",className:"ml-2",children:"Anonymous"})]}),(0,d.jsxs)("div",{children:[(0,d.jsx)(m,{id:"authenticated",value:"Authenticated",name:"securityGroup",checked:j.requiredSecurityGroup===i.hh.Authenticated,onChange:C}),(0,d.jsx)("label",{htmlFor:"authenticated",className:"ml-2",children:"Authenticated"})]}),(0,d.jsxs)("div",{children:[(0,d.jsx)(m,{id:"connected",value:"Connected",name:"securityGroup",checked:j.requiredSecurityGroup===i.hh.Connected,onChange:C}),(0,d.jsx)("label",{htmlFor:"connected",className:"ml-2",children:"Connected"})]})]})})}),(0,d.jsxs)("div",{className:"-mx-2 px-4 py-3 sm:flex sm:flex-row-reverse sm:px-6",children:[(0,d.jsx)(u.Z,{className:"mx-2",onClick:function(){h(j)},children:null!==n&&void 0!==n?n:"Save"}),(0,d.jsx)(u.Z,{className:"mx-2",type:"secondary",onClick:g,children:(0,o.t)("Cancel")})]})]})})})]});return(0,l.createPortal)(w,p)},x=function(e){var t=e.className;return(0,d.jsx)("svg",{xmlns:"http://www.w3.org/2000/svg",viewBox:"0 0 448 512",fill:"currentColor",className:t,children:(0,d.jsx)("path",{d:"M377.7 338.8l37.15-92.87C419 235.4 411.3 224 399.1 224h-57.48C348.5 209.2 352 193 352 176c0-4.117-.8359-8.057-1.217-12.08C390.7 155.1 416 142.3 416 128c0-16.08-31.75-30.28-80.31-38.99C323.8 45.15 304.9 0 277.4 0c-10.38 0-19.62 4.5-27.38 10.5c-15.25 11.88-36.75 11.88-52 0C190.3 4.5 181.1 0 170.7 0C143.2 0 124.4 45.16 112.5 88.98C63.83 97.68 32 111.9 32 128c0 14.34 25.31 27.13 65.22 35.92C96.84 167.9 96 171.9 96 176C96 193 99.47 209.2 105.5 224H48.02C36.7 224 28.96 235.4 33.16 245.9l37.15 92.87C27.87 370.4 0 420.4 0 477.3C0 496.5 15.52 512 34.66 512H413.3C432.5 512 448 496.5 448 477.3C448 420.4 420.1 370.4 377.7 338.8zM176 479.1L128 288l64 32l16 32L176 479.1zM271.1 479.1L240 352l16-32l64-32L271.1 479.1zM320 186C320 207 302.8 224 281.6 224h-12.33c-16.46 0-30.29-10.39-35.63-24.99C232.1 194.9 228.4 192 224 192S215.9 194.9 214.4 199C209 213.6 195.2 224 178.8 224h-12.33C145.2 224 128 207 128 186V169.5C156.3 173.6 188.1 176 224 176s67.74-2.383 96-6.473V186z"})})},h=function(e){var t=e.className;return(0,d.jsx)("svg",{xmlns:"http://www.w3.org/2000/svg",viewBox:"0 0 576 512",fill:"currentColor",className:t,children:(0,d.jsx)("path",{d:"M96 304.1c0-12.16 4.971-23.83 13.64-32.01l72.13-68.08c1.65-1.555 3.773-2.311 5.611-3.578C177.1 176.8 155 160 128 160H64C28.65 160 0 188.7 0 224v96c0 17.67 14.33 32 31.1 32L32 480c0 17.67 14.33 32 32 32h64c17.67 0 32-14.33 32-32v-96.39l-50.36-47.53C100.1 327.9 96 316.2 96 304.1zM480 128c35.38 0 64-28.62 64-64s-28.62-64-64-64s-64 28.62-64 64S444.6 128 480 128zM96 128c35.38 0 64-28.62 64-64S131.4 0 96 0S32 28.62 32 64S60.63 128 96 128zM444.4 295.3L372.3 227.3c-3.49-3.293-8.607-4.193-13.01-2.299C354.9 226.9 352 231.2 352 236V272H224V236c0-4.795-2.857-9.133-7.262-11.03C212.3 223.1 207.2 223.1 203.7 227.3L131.6 295.3c-4.805 4.535-4.805 12.94 0 17.47l72.12 68.07c3.49 3.291 8.607 4.191 13.01 2.297C221.1 381.3 224 376.9 224 372.1V336h128v36.14c0 4.795 2.857 9.135 7.262 11.04c4.406 1.893 9.523 .9922 13.01-2.299l72.12-68.07C449.2 308.3 449.2 299.9 444.4 295.3zM512 160h-64c-26.1 0-49.98 16.77-59.38 40.42c1.842 1.271 3.969 2.027 5.623 3.588l72.12 68.06C475 280.2 480 291.9 480 304.1c.002 12.16-4.969 23.83-13.64 32.01L416 383.6V480c0 17.67 14.33 32 32 32h64c17.67 0 32-14.33 32-32v-128c17.67 0 32-14.33 32-32V224C576 188.7 547.3 160 512 160z"})})},g=function(e){var t=e.className;return(0,d.jsx)("svg",{xmlns:"http://www.w3.org/2000/svg",viewBox:"0 0 512 512",fill:"currentColor",className:t,children:(0,d.jsx)("path",{d:"M352 256C352 278.2 350.8 299.6 348.7 320H163.3C161.2 299.6 159.1 278.2 159.1 256C159.1 233.8 161.2 212.4 163.3 192H348.7C350.8 212.4 352 233.8 352 256zM503.9 192C509.2 212.5 512 233.9 512 256C512 278.1 509.2 299.5 503.9 320H380.8C382.9 299.4 384 277.1 384 256C384 234 382.9 212.6 380.8 192H503.9zM493.4 160H376.7C366.7 96.14 346.9 42.62 321.4 8.442C399.8 29.09 463.4 85.94 493.4 160zM344.3 160H167.7C173.8 123.6 183.2 91.38 194.7 65.35C205.2 41.74 216.9 24.61 228.2 13.81C239.4 3.178 248.7 0 256 0C263.3 0 272.6 3.178 283.8 13.81C295.1 24.61 306.8 41.74 317.3 65.35C328.8 91.38 338.2 123.6 344.3 160H344.3zM18.61 160C48.59 85.94 112.2 29.09 190.6 8.442C165.1 42.62 145.3 96.14 135.3 160H18.61zM131.2 192C129.1 212.6 127.1 234 127.1 256C127.1 277.1 129.1 299.4 131.2 320H8.065C2.8 299.5 0 278.1 0 256C0 233.9 2.8 212.5 8.065 192H131.2zM194.7 446.6C183.2 420.6 173.8 388.4 167.7 352H344.3C338.2 388.4 328.8 420.6 317.3 446.6C306.8 470.3 295.1 487.4 283.8 498.2C272.6 508.8 263.3 512 255.1 512C248.7 512 239.4 508.8 228.2 498.2C216.9 487.4 205.2 470.3 194.7 446.6H194.7zM190.6 503.6C112.2 482.9 48.59 426.1 18.61 352H135.3C145.3 415.9 165.1 469.4 190.6 503.6V503.6zM321.4 503.6C346.9 469.4 366.7 415.9 376.7 352H493.4C463.4 426.1 399.8 482.9 321.4 503.6V503.6z"})})},p=n(6910),v=function(e){var t=e.acl,n=e.onChange,l=(0,s.useState)(!1),c=(0,a.Z)(l,2),u=c[0],m=c[1],v=function(e){return t.requiredSecurityGroup===i.hh.Anonymous?(0,d.jsx)(g,(0,r.Z)({},e)):t.requiredSecurityGroup===i.hh.Authenticated?(0,d.jsx)(x,(0,r.Z)({},e)):t.requiredSecurityGroup===i.hh.Connected?(0,d.jsx)(h,(0,r.Z)({},e)):(t.requiredSecurityGroup,i.hh.Owner,(0,d.jsx)(p.Z,(0,r.Z)({},e)))};return(0,d.jsxs)(d.Fragment,{children:[(0,d.jsx)("button",{title:t.requiredSecurityGroup,className:"mr-2 inline-block ".concat(n?"":"cursor-default"),onClick:function(){return n&&m(!0)},children:(0,d.jsx)(v,{className:"h-5 w-5"})}),(0,d.jsx)(f,{acl:t,isOpen:u,title:(0,o.t)("Edit security"),onCancel:function(){m(!1)},onConfirm:function(e){n(e),m(!1)}})]})}},8923:function(e,t,n){var r=n(4164),a=n(4990),i=n(3412),s=n(1088),o=n(184);t.Z=function(e){var t=e.title,n=e.confirmText,l=e.children,c=e.needConfirmation,u=e.onConfirm,d=e.onCancel,m=(0,i.Z)("modal-container");if(!c)return null;var f=(0,o.jsxs)("div",{className:"relative z-50","aria-labelledby":"modal-title",role:"dialog","aria-modal":"true",children:[(0,o.jsx)("div",{className:"fixed inset-0 bg-gray-500 bg-opacity-75 transition-opacity"}),(0,o.jsx)("div",{className:"fixed inset-0 z-10 overflow-y-auto",onClick:d,children:(0,o.jsx)("div",{className:"flex min-h-full items-end justify-center p-4 text-center sm:items-center sm:p-0",children:(0,o.jsxs)("div",{className:"relative transform overflow-hidden rounded-lg bg-white text-left shadow-xl transition-all dark:bg-black sm:my-8 sm:w-full sm:max-w-lg",onClick:function(e){return e.preventDefault(),!1},children:[(0,o.jsx)("div",{className:"bg-white px-4 pt-5 pb-4 dark:bg-black sm:p-6 sm:pb-4",children:(0,o.jsxs)("div",{className:"sm:flex sm:items-start",children:[(0,o.jsx)("div",{className:"mx-auto flex h-12 w-12 flex-shrink-0 items-center justify-center rounded-full text-red-400 sm:mx-0 sm:h-10 sm:w-10",children:(0,o.jsx)(s.Z,{})}),(0,o.jsxs)("div",{className:"mt-3 text-center sm:mt-0 sm:ml-4 sm:text-left",children:[(0,o.jsx)("h3",{className:"text-lg font-medium leading-6 text-gray-900 dark:text-slate-50",id:"modal-title",children:t}),(0,o.jsx)("div",{className:"mt-2",children:l})]})]})}),(0,o.jsxs)("div",{className:"bg-gray-50 px-4 py-3 dark:bg-slate-900 sm:flex sm:flex-row-reverse sm:px-6",children:[(0,o.jsx)("button",{type:"button",className:"inline-flex w-full justify-center rounded-md border border-transparent bg-red-600 px-4 py-2 text-base font-medium text-white shadow-sm hover:bg-red-700 focus:outline-none focus:ring-2 focus:ring-red-500 focus:ring-offset-2 sm:ml-3 sm:w-auto sm:text-sm",onClick:u,children:n}),(0,o.jsx)("button",{type:"button",className:"mt-3 inline-flex w-full justify-center rounded-md border border-gray-300 bg-white px-4 py-2 text-base font-medium text-gray-700 shadow-sm hover:bg-gray-50 focus:outline-none focus:ring-2 focus:ring-indigo-500 focus:ring-offset-2 dark:border-gray-800 dark:bg-slate-700 dark:text-white sm:mt-0 sm:ml-3 sm:w-auto sm:text-sm",onClick:d,children:(0,a.t)("Cancel")})]})]})})})]});return(0,r.createPortal)(f,m)}},6691:function(e,t,n){var r=n(4165),a=n(5861),i=n(885),s=n(2791),o=n(4164),l=n(4990),c=n(9779),u=n(3412),d=n(8191),m=n(184);t.Z=function(e){var t=e.title,n=e.confirmText,f=e.isOpen,x=e.acl,h=e.targetDrive,g=e.onConfirm,p=e.onCancel,v=(0,u.Z)("modal-container"),b=(0,c.Z)(),j=(0,i.Z)(b,2)[1],y=j.mutate,C=j.status,w=(0,s.useState)(),N=(0,i.Z)(w,2),Z=N[0],k=N[1];if(!f)return null;var S=function(){var e=(0,a.Z)((0,r.Z)().mark((function e(){var t;return(0,r.Z)().wrap((function(e){for(;;)switch(e.prev=e.next){case 0:return e.t0=Uint8Array,e.next=3,Z.arrayBuffer();case 3:e.t1=e.sent,t=new e.t0(e.t1),y({acl:x,bytes:t,fileId:void 0,targetDrive:h},{onSuccess:g});case 6:case"end":return e.stop()}}),e)})));return function(){return e.apply(this,arguments)}}(),I=(0,m.jsxs)("div",{className:"relative z-50","aria-labelledby":"modal-title",role:"dialog","aria-modal":"true",children:[(0,m.jsx)("div",{className:"fixed inset-0 bg-gray-500 bg-opacity-75 transition-opacity"}),(0,m.jsx)("div",{className:"fixed inset-0 z-10 overflow-y-auto",children:(0,m.jsx)("div",{className:"flex min-h-full items-end justify-center p-4 text-center sm:items-center sm:p-0",children:(0,m.jsxs)("div",{className:"relative transform overflow-hidden rounded-lg bg-white text-left shadow-xl transition-all dark:bg-black sm:my-8 sm:w-full sm:max-w-lg",children:[(0,m.jsx)("div",{className:"bg-white px-4 pt-5 pb-4 dark:bg-black sm:p-6 sm:pb-4",children:(0,m.jsx)("div",{className:"sm:flex sm:items-start",children:(0,m.jsxs)("div",{className:"mt-3 text-center sm:mt-0 sm:ml-4 sm:text-left",children:[(0,m.jsx)("h3",{className:"mb-3 text-lg font-medium leading-6 text-gray-900 dark:text-slate-50",id:"modal-title",children:t}),(0,m.jsx)("input",{onChange:function(e){var t=e.target.files[0];t&&k(t)},type:"file",accept:"image/png, image/jpeg, image/tiff, image/webp",className:"w-full rounded border border-gray-300 bg-white py-1 px-3 text-base leading-8 text-gray-700 outline-none transition-colors duration-200 ease-in-out focus:border-indigo-500 focus:ring-2 focus:ring-indigo-200 dark:border-gray-700 dark:bg-gray-800 dark:text-gray-100"})]})})}),(0,m.jsxs)("div",{className:"-mx-2 px-4 py-3 sm:flex sm:flex-row-reverse sm:px-6",children:[(0,m.jsx)(d.Z,{className:"mx-2",onClick:S,state:C,children:null!==n&&void 0!==n?n:"Add"}),(0,m.jsx)(d.Z,{className:"mx-2",type:"secondary",onClick:p,children:(0,l.t)("Cancel")})]})]})})})]});return(0,o.createPortal)(I,v)}},977:function(e,t,n){n.d(t,{Z:function(){return f}});var r=n(885),a=n(2791),i=n(4990),s=n(9779),o=n(8923),l=n(1088),c=n(184),u=function(e){var t=e.className;return(0,c.jsx)("svg",{xmlns:"http://www.w3.org/2000/svg",viewBox:"0 0 512 512",fill:"currentColor",className:t,children:(0,c.jsx)("path",{d:"M421.7 220.3L188.5 453.4L154.6 419.5L158.1 416H112C103.2 416 96 408.8 96 400V353.9L92.51 357.4C87.78 362.2 84.31 368 82.42 374.4L59.44 452.6L137.6 429.6C143.1 427.7 149.8 424.2 154.6 419.5L188.5 453.4C178.1 463.8 165.2 471.5 151.1 475.6L30.77 511C22.35 513.5 13.24 511.2 7.03 504.1C.8198 498.8-1.502 489.7 .976 481.2L36.37 360.9C40.53 346.8 48.16 333.9 58.57 323.5L291.7 90.34L421.7 220.3zM492.7 58.75C517.7 83.74 517.7 124.3 492.7 149.3L444.3 197.7L314.3 67.72L362.7 19.32C387.7-5.678 428.3-5.678 453.3 19.32L492.7 58.75z"})})},d=n(7134),m=n(6691),f=function(e){var t=e.targetDrive,n=e.acl,f=e.onChange,x=e.defaultValue,h=e.name,g=(0,s.Z)("string"===typeof x?x:void 0,t),p=(0,r.Z)(g,3),v=p[0],b=v.data,j=v.isLoading,y=p[2].mutate,C=(0,a.useState)(!1),w=(0,r.Z)(C,2),N=w[0],Z=w[1],k=(0,a.useState)(!1),S=(0,r.Z)(k,2),I=S[0],O=S[1];return j?(0,c.jsx)("div",{className:"aspect-square max-w-[20rem] animate-pulse bg-slate-100 dark:bg-slate-700"}):(0,c.jsxs)(c.Fragment,{children:[b?(0,c.jsx)("div",{className:"flex",children:(0,c.jsxs)("div",{className:"relative mr-auto",children:[(0,c.jsx)("button",{className:"absolute top-2 right-2 rounded-full bg-white p-2",onClick:function(e){return e.preventDefault(),Z(!0),!1},children:(0,c.jsx)(u,{className:"h-4 w-4 text-black"})}),(0,c.jsx)("button",{className:"absolute bottom-2 right-2 rounded-full bg-red-200 p-2",onClick:function(){return O(!0),!1},children:(0,c.jsx)(d.Z,{className:"h-4 w-4 text-black"})}),(0,c.jsx)("img",{src:b,alt:b,className:"max-h-[20rem]",onClick:function(){Z(!0)}})]})}):(0,c.jsxs)("div",{className:"relative flex aspect-video max-w-[20rem] cursor-pointer bg-slate-100 dark:bg-slate-700",onClick:function(e){e.preventDefault(),Z(!0)},children:[(0,c.jsx)(l.Z,{className:"m-auto h-8 w-8"}),(0,c.jsx)("p",{className:"absolute inset-0 top-auto pb-5 text-center text-slate-400",children:(0,i.t)("No image selected")})]}),(0,c.jsx)(m.Z,{acl:n,isOpen:N,targetDrive:t,title:(0,i.t)("Insert image"),confirmText:(0,i.t)("Add"),onCancel:function(){return Z(!1)},onConfirm:function(e){f({target:{name:h,value:e}}),Z(!1)}}),(0,c.jsx)(o.Z,{title:"Remove Current Image",confirmText:"Permanently remove",needConfirmation:I,onConfirm:function(){y({fileId:"string"===typeof x?x:void 0,targetDrive:t},{onSuccess:function(){f({target:{name:h,value:""}})}})},onCancel:function(){O(!1)},children:(0,c.jsx)("p",{className:"text-sm text-gray-500",children:(0,i.t)("Are you sure you want to remove the current file? This action cannot be undone.")})})]})}},3225:function(e,t,n){var r=n(1413),a=n(184);t.Z=function(e){return(0,a.jsx)("select",(0,r.Z)((0,r.Z)({},e),{},{className:"w-full rounded border border-gray-300 bg-white py-1 px-3 text-base leading-8 text-gray-700 outline-none transition-colors duration-200 ease-in-out focus:border-indigo-500 focus:ring-2 focus:ring-indigo-200 dark:border-gray-700 dark:bg-gray-800 dark:text-gray-100 ".concat(e.className),children:e.children}))}},8491:function(e,t,n){var r=n(184);t.Z=function(e){var t=e.title,n=e.className,a=e.children,i=e.isOpaqueBg,s=void 0!==i&&i;return(0,r.jsxs)("section",{className:"my-5 rounded-md ".concat(s?"rounded-lg border-[1px] border-gray-200 border-opacity-80 dark:border-gray-700":"bg-slate-50 dark:bg-slate-800"," px-5  dark:text-slate-300 ").concat(null!==n&&void 0!==n?n:""),children:[t?(0,r.jsx)("div",{className:"relative border-b-[1px] border-gray-200 border-opacity-80 py-5 transition-all duration-300 dark:border-gray-700",children:(0,r.jsx)("h3",{className:"text-2xl dark:text-white",children:t})}):null,(0,r.jsx)("div",{className:"py-5 ",children:a})]})}},2653:function(e,t,n){n.r(t),n.d(t,{default:function(){return H}});var r=n(1413),a=n(2982),i=n(885),s=n(2791),o=n(6871),l=n(7221),c=n(3431),u=n(184),d=function(e){var t=e.className,n=e.items,r=e.onChange;return(0,u.jsx)("div",{className:"flex ".concat(t),children:n.map((function(e){var t;return(0,u.jsx)("a",{className:"flex-grow cursor-pointer border-b-2 py-2 px-1 text-lg ".concat(e.isActive?"border-indigo-500 text-indigo-500 dark:text-indigo-400":"border-gray-300 transition-colors duration-300 hover:border-indigo-400 dark:border-gray-800 hover:dark:border-indigo-600"," ").concat(null!==(t=e.className)&&void 0!==t?t:""),onClick:function(){r(e.key)},children:e.title},e.key)}))})},m=n(1803),f=n(4990),x=n(9077),h=n(8191),g=n(3225),p=n(8491),v=n(2878),b=n(977),j=n(6916),y=n(2465),C=function(e){var t=e.attribute,n=e.onChange,r=(0,s.useMemo)((function(){return(0,v.Z)(n,500)}),[n]),a=Object.keys(m.Qv).filter((function(e){return!isNaN(Number(e))})),i=Object.keys(m.Qv).filter((function(e){return isNaN(Number(e))}));switch(t.type){case m.H1.Name.type.toString():return(0,u.jsxs)("div",{className:"-mx-2 flex flex-row",children:[(0,u.jsxs)("div",{className:"mb-5 w-2/5 px-2",children:[(0,u.jsx)("label",{htmlFor:"given-name",children:(0,f.t)("Given name")}),(0,u.jsx)(j.Z,{id:"given-name",name:"givenName",defaultValue:t.data.givenName,onChange:r})]}),(0,u.jsxs)("div",{className:"mb-5 w-3/5 px-2",children:[(0,u.jsx)("label",{htmlFor:"sur-name",children:(0,f.t)("Surname")}),(0,u.jsx)(j.Z,{id:"sur-name",name:"surname",defaultValue:t.data.surname,onChange:r})]})]});case m.H1.Photo.type.toString():return(0,u.jsxs)("div",{className:"mb-5",children:[(0,u.jsx)("label",{htmlFor:"profileImageId",children:(0,f.t)("Profile Image")}),(0,u.jsx)(b.Z,{id:"profileImageId",name:"profileImageId",defaultValue:t.data.profileImageId,onChange:r,acl:t.acl,targetDrive:(0,m.H3)(m.P.StandardProfileId.toString())})]});case m.H1.InstagramUsername.type.toString():case m.H1.TiktokUsername.type.toString():case m.H1.TwitterUsername.type.toString():case m.H1.LinkedInUsername.type.toString():case m.H1.FacebookUsername.type.toString():return(0,u.jsx)(u.Fragment,{children:(0,u.jsxs)("div",{className:"mb-5",children:[(0,u.jsx)("label",{htmlFor:"handle",children:t.typeDefinition.name}),(0,u.jsx)(j.Z,{id:"handle",name:t.typeDefinition.name.toLowerCase(),defaultValue:t.data[t.typeDefinition.name.toLowerCase()],onChange:r})]})});case m.H1.FullBio.type.toString():return(0,u.jsxs)(u.Fragment,{children:[(0,u.jsxs)("div",{className:"mb-5",children:[(0,u.jsx)("label",{htmlFor:"short-bio",children:(0,f.t)("Bio")}),(0,u.jsx)(j.Z,{id:"short-bio",name:"short_bio",defaultValue:t.data.short_bio,onChange:r})]}),(0,u.jsxs)("div",{className:"mb-5",children:[(0,u.jsx)("label",{htmlFor:"full-bio",children:(0,f.t)("Full bio")}),(0,u.jsx)(y.Z,{id:"full-bio",name:"full_bio",defaultValue:t.data.full_bio,onChange:r})]})]});case m.H1.ShortBio.type.toString():return(0,u.jsx)(u.Fragment,{children:(0,u.jsxs)("div",{className:"mb-5",children:[(0,u.jsx)("label",{htmlFor:"short-bio",children:(0,f.t)("Bio")}),(0,u.jsx)(j.Z,{id:"short-bio",name:"short_bio",defaultValue:t.data.short_bio,onChange:r})]})});case m.H1.CreditCard.type.toString():return(0,u.jsxs)(u.Fragment,{children:[(0,u.jsxs)("div",{className:"mb-5",children:[(0,u.jsx)("label",{htmlFor:"cc-alias",children:(0,f.t)("Alias")}),(0,u.jsx)(j.Z,{id:"cc-alias",name:"cc_alias",defaultValue:t.data.cc_alias,onChange:r})]}),(0,u.jsxs)("div",{className:"mb-5",children:[(0,u.jsx)("label",{htmlFor:"cc-name",children:(0,f.t)("Name on Card")}),(0,u.jsx)(j.Z,{id:"cc-name",name:"cc_name",defaultValue:t.data.cc_name,onChange:r})]}),(0,u.jsxs)("div",{className:"mb-5",children:[(0,u.jsx)("label",{htmlFor:"cc-number",children:(0,f.t)("Credit card number")}),(0,u.jsx)(j.Z,{id:"cc-number",name:"cc_number",defaultValue:t.data.cc_number,onChange:r})]})]});case m.Dg.HomePage.toString():return(0,u.jsxs)(u.Fragment,{children:[(0,u.jsxs)("div",{className:"mb-5",children:[(0,u.jsx)("label",{htmlFor:"headerImageUrl",children:(0,f.t)("Header Image")}),(0,u.jsx)(b.Z,{id:"headerImageUrl",name:"headerImageUrl",defaultValue:t.data.headerImageUrl,onChange:r,acl:t.acl,targetDrive:(0,m.H3)(m.Ib.DefaultDriveId.toString())})]}),(0,u.jsxs)("div",{className:"mb-5",children:[(0,u.jsx)("label",{htmlFor:"tagLine",children:(0,f.t)("Tag Line")}),(0,u.jsx)(j.Z,{id:"tagLine",name:"tagLine",defaultValue:t.data.tagLine,onChange:r})]}),(0,u.jsxs)("div",{className:"mb-5",children:[(0,u.jsx)("label",{htmlFor:"leadText",children:(0,f.t)("Lead Text")}),(0,u.jsx)(y.Z,{id:"leadText",name:"leadText",defaultValue:t.data.leadText,onChange:r})]})]});case m.Dg.Theme.toString():return(0,u.jsx)(u.Fragment,{children:(0,u.jsxs)("div",{className:"mb-5",children:[(0,u.jsx)("label",{htmlFor:"themeId",children:(0,f.t)("Theme")}),(0,u.jsxs)(g.Z,{id:"themeId",name:"themeId",defaultValue:t.data.themeId,onChange:r,children:[(0,u.jsx)("option",{children:(0,f.t)("Make a selection")}),a.map((function(e,t){return(0,u.jsx)("option",{value:e,children:(0,f.t)(i[t])},e)}))]})]})});default:return(0,u.jsx)(u.Fragment,{children:Object.keys(t.data).map((function(e){return(0,u.jsxs)("p",{className:"whitespace-pre-line",children:[e,": ",t.data[e]]},e)}))})}},w=function(e){var t,n=e.profileId,a=e.sectionId,o=(0,s.useState)(!1),l=(0,i.Z)(o,2),c=l[0],d=l[1],v=(0,s.useState)(),b=(0,i.Z)(v,2),j=b[0],y=b[1],w=(0,x.Z)({}),N=(0,i.Z)(w,2)[1],Z=N.mutate,k=N.isLoading,S=N.isError,I=N.isSuccess,O=function(){d(!1),y(void 0)};return(0,u.jsx)(u.Fragment,{children:c?(0,u.jsxs)(p.Z,{title:"New".concat(j?":":""," ").concat(null!==(t=null===j||void 0===j?void 0:j.typeDefinition.name)&&void 0!==t?t:""),isOpaqueBg:!0,children:[void 0===j?(0,u.jsxs)("div",{className:"mb-5",children:[(0,u.jsx)("label",{htmlFor:"type",children:(0,f.t)("Attribute Type")}),(0,u.jsxs)(g.Z,{id:"type",onChange:function(e){!function(e){var t=Object.values(m.H1).find((function(t){return t.type.toString()===e}));y({id:"",type:e,sectionId:a,priority:-1,data:{},typeDefinition:t,profileId:n,acl:{requiredSecurityGroup:m.hh.Owner}})}(e.target.value)},children:[(0,u.jsx)("option",{children:(0,f.t)("Make a selection")}),Object.values(m.H1).map((function(e){return(0,u.jsx)("option",{value:e.type.toString(),children:e.name},e.type.toString())}))]})]}):(0,u.jsx)(C,{attribute:j,onChange:function(e){if(j){var t=(0,r.Z)({},j);t.data[e.target.name]=e.target.value,y(t)}}}),(0,u.jsxs)("div",{className:"flex flex-row",children:[(0,u.jsx)(h.Z,{type:"secondary",className:"ml-auto",onClick:O,children:(0,f.t)("Cancel")}),(0,u.jsx)(h.Z,{type:"primary",icon:"plus",className:"ml-2",onClick:function(){console.log(j),Z(j,{onSuccess:function(){O()}})},state:k?"loading":I?"success":S?"failed":void 0,children:(0,f.t)("Add")})]})]}):(0,u.jsx)("div",{className:"flex flex-row",children:(0,u.jsx)(h.Z,{type:"primary",icon:"plus",className:"mx-auto min-w-[9rem]",onClick:function(){return d(!0)},children:(0,f.t)("Add Attribute")})})})},N=n(5207),Z=n(3004),k=function(e){var t=e.className;return(0,u.jsx)("svg",{xmlns:"http://www.w3.org/2000/svg",viewBox:"0 0 512 512",fill:"currentColor",className:t,children:(0,u.jsx)("path",{d:"M215.1 272h-136c-12.94 0-24.63 7.797-29.56 19.75C45.47 303.7 48.22 317.5 57.37 326.6l30.06 30.06l-78.06 78.07c-12.5 12.5-12.5 32.75-.0012 45.25l22.62 22.62c12.5 12.5 32.76 12.5 45.26 .0013l78.06-78.07l30.06 30.06c6.125 6.125 14.31 9.367 22.63 9.367c4.125 0 8.279-.7891 12.25-2.43c11.97-4.953 19.75-16.62 19.75-29.56V296C239.1 282.7 229.3 272 215.1 272zM296 240h136c12.94 0 24.63-7.797 29.56-19.75c4.969-11.97 2.219-25.72-6.938-34.87l-30.06-30.06l78.06-78.07c12.5-12.5 12.5-32.76 .0002-45.26l-22.62-22.62c-12.5-12.5-32.76-12.5-45.26-.0003l-78.06 78.07l-30.06-30.06c-9.156-9.141-22.87-11.84-34.87-6.937c-11.97 4.953-19.75 16.62-19.75 29.56v135.1C272 229.3 282.7 240 296 240z"})})},S=n(5660),I=n(8923),O=n(2723),F=function(e){var t=e.attribute,n=e.className,a=(0,s.useState)(!1),o=(0,i.Z)(a,2),l=o[0],c=o[1],d=(0,x.Z)({}),m=(0,i.Z)(d,3),g=m[1],v=g.mutate,b=g.status,j=g.isLoading,y=g.isError,w=g.isSuccess,N=m[2].mutate,Z=(0,s.useState)((0,r.Z)({},t)),k=(0,i.Z)(Z,2),F=k[0],D=k[1],L=function(e){v(e)},M=function(e){var t=(0,r.Z)({},F);t[e.target.name]=e.target.value,D(t),L(t)};return(0,u.jsxs)(u.Fragment,{children:[(0,u.jsxs)(p.Z,{isOpaqueBg:!0,title:(0,u.jsxs)(u.Fragment,{children:[(0,u.jsx)(O.Z,{acl:F.acl,onChange:function(e){M({target:{name:"acl",value:e}})}})," ",F.typeDefinition.name]}),className:"".concat(n," relative"),children:[(0,u.jsx)(C,{attribute:F,onChange:M}),(0,u.jsxs)("div",{className:"top-5 right-5 flex flex-row md:absolute",children:[(0,u.jsx)(h.Z,{type:"remove",icon:"trash",className:"ml-auto",onClick:function(){c(!0)}}),(0,u.jsx)(h.Z,{state:j?"loading":w?"success":y?"failed":"success",type:"primary",className:"ml-2",onClick:function(){return L(F)},children:(0,f.t)("Save")})]}),(0,u.jsx)(S.Z,{className:"mt-2 text-right sm:mt-0",state:b})]}),(0,u.jsx)(I.Z,{title:"Remove Attribute",confirmText:"Permanently remove",needConfirmation:l,onConfirm:function(){c(!1),N(t)},onCancel:function(){c(!1)},children:(0,u.jsxs)("p",{className:"text-sm text-gray-500",children:[(0,f.t)("Are you sure you want to remove your")," ",t.typeDefinition.name," ",(0,f.t)("attribute. This action cannot be undone.")]})})]})},D=function(e){var t=e.attributes,n=e.groupTitle,r=(0,s.useState)(1===t.length),a=(0,i.Z)(r,2),o=a[0],l=a[1];return 1===t.length?(0,u.jsx)(F,{attribute:t[0]}):(0,u.jsxs)("div",{className:"relative my-10 overflow-x-hidden ".concat(o?"":"cursor-pointer transition-transform"),style:{paddingBottom:"".concat(10*t.length,"px")},onClick:function(){o||l(!0)},children:[(0,u.jsxs)("h2",{onClick:function(){return l(!1)},className:"cursor-pointer text-2xl",children:[(0,u.jsx)(k,{className:"inline-block h-4 w-4 ".concat(o?"opacity-100":"opacity-0")})," ",n," ",(0,u.jsxs)("small",{children:["(",t.length,")"]})]}),(0,u.jsx)("div",{className:"border-l-[16px] border-slate-50 pt-5 transition-transform dark:border-slate-600 ".concat(o?"pl-5":"-translate-x-4 hover:translate-x-0"),children:t.map((function(e,t){return(0,u.jsx)("span",{className:o||0===t?"":"absolute left-0 right-0 top-0 bg-white shadow-slate-50 dark:bg-slate-800",style:{transform:"translateX(".concat(4*t,"px) translateY(").concat(10*t,"px)")},children:(0,u.jsx)(F,{attribute:e,className:o?"mt-0 mb-5":"pointer-events-none my-0 opacity-50 grayscale"})},e.id)}))})]})},L=function(e){var t=e.profileDefinition,n=(0,s.useState)(""),o=(0,i.Z)(n,2),l=o[0],c=o[1];return(0,u.jsx)(p.Z,{title:"New: section",isOpaqueBg:!0,children:(0,u.jsxs)("form",{onSubmit:function(e){e.preventDefault();var n={sectionId:"",attributes:[],priority:Math.max.apply(Math,(0,a.Z)(t.sections.map((function(e){return e.priority}))))+1,isSystemSection:!1,name:l},i=(0,r.Z)({},t);return i.sections.push(n),console.log("Should create: ",i),!1},children:[(0,u.jsxs)("div",{className:"mb-5",children:[(0,u.jsx)("label",{htmlFor:"name",children:(0,f.t)("Name")}),(0,u.jsx)(j.Z,{id:"name",name:"sectionName",onChange:function(e){c(e.target.value)},required:!0})]}),(0,u.jsx)("div",{className:"flex flex-row",children:(0,u.jsx)(h.Z,{className:"ml-auto",children:(0,f.t)("add section")})})]})})},M=function(e){var t=e.section,n=e.profileId,r=(0,l.Z)({profileId:n,sectionId:t.sectionId}),s=(0,i.Z)(r,1)[0],o=s.data,c=s.isLoading;if(!o||c)return(0,u.jsx)(u.Fragment,{children:"Loading"});var d=o.reduce((function(e,t){return-1!==e.indexOf(t.type)?e:[].concat((0,a.Z)(e),[t.type])}),[]).map((function(e){var t=o.filter((function(t){return t.type===e}));return{name:t[0].typeDefinition.name,attributes:t}}));return(0,u.jsxs)(u.Fragment,{children:[o.length?d.map((function(e){return(0,u.jsx)(D,{groupTitle:e.name,attributes:e.attributes},e.name)})):(0,u.jsx)("div",{className:"py-5",children:(0,f.t)("section-empty-attributes")}),(0,u.jsx)(w,{profileId:n,sectionId:t.sectionId})]})},H=function(){var e=(0,c.Z)(),t=e.data,n=e.isLoading,r=(0,o.UO)().profileKey,l=null===t||void 0===t?void 0:t.definitions.find((function(e){return e.slug===r})),m=(0,s.useState)(null!==l&&void 0!==l&&l.sections?l.sections[0].sectionId:""),x=(0,i.Z)(m,2),h=x[0],g=x[1];if(n)return(0,u.jsx)(u.Fragment,{children:"Loading"});if(!t)return(0,u.jsx)(u.Fragment,{children:(0,f.t)("no-data-found")});if(!l)return(0,u.jsx)(u.Fragment,{children:"Incorrect profile path"});var p="new"===h?void 0:l.sections.find((function(e){return e.sectionId===h}))||l.sections[0];return(0,u.jsxs)(u.Fragment,{children:[(0,u.jsx)(Z.Z,{title:l.name}),(0,u.jsx)(d,{className:"mt-5",items:[].concat((0,a.Z)(l.sections.map((function(e,t){return{title:e.name,key:e.sectionId,isActive:h?h===e.sectionId:0===t}}))),[{title:(0,u.jsx)(N.Z,{className:"h-5 w-5"}),key:"new",isActive:h?"new"===h:!l.sections.length,className:"flex-grow-0"}]),onChange:function(e){g(e)}}),"new"===h?(0,u.jsx)(L,{profileDefinition:l}):p&&(0,u.jsx)(M,{section:p,profileId:l.profileId},p.sectionId)]})}},9779:function(e,t,n){var r=n(4165),a=n(5861),i=n(7408),s=n(1803),o=n(6117),l={alias:s.Ib.BlogMainContentDriveId.toString(),type:s.Hm.DriveType.toString()};t.Z=function(e,t){var n=(0,o.Z)().getSharedSecret,c=(0,i.useQueryClient)(),u=new s.KU({api:s.Ii.Owner,sharedSecret:n()}),d=function(){var e=(0,a.Z)((0,r.Z)().mark((function e(t,n){return(0,r.Z)().wrap((function(e){for(;;)switch(e.prev=e.next){case 0:if(void 0!==t&&""!==t){e.next=2;break}return e.abrupt("return");case 2:return e.next=4,u.mediaProvider.getDecryptedImageUrl(null!==n&&void 0!==n?n:l,t);case 4:return e.abrupt("return",e.sent);case 5:case"end":return e.stop()}}),e)})));return function(t,n){return e.apply(this,arguments)}}(),m=function(){var e=(0,a.Z)((0,r.Z)().mark((function e(t){var n,a,i,o,c,d,m;return(0,r.Z)().wrap((function(e){for(;;)switch(e.prev=e.next){case 0:return n=t.bytes,a=t.targetDrive,i=void 0===a?l:a,o=t.acl,c=void 0===o?{requiredSecurityGroup:s.hh.Anonymous}:o,d=t.fileId,m=void 0===d?void 0:d,e.next=3,u.mediaProvider.uploadImage(i,void 0,c,n,m);case 3:return e.abrupt("return",e.sent);case 4:case"end":return e.stop()}}),e)})));return function(t){return e.apply(this,arguments)}}(),f=function(){var e=(0,a.Z)((0,r.Z)().mark((function e(t){var n,a,i;return(0,r.Z)().wrap((function(e){for(;;)switch(e.prev=e.next){case 0:return n=t.targetDrive,a=void 0===n?l:n,i=t.fileId,e.next=3,u.mediaProvider.removeImage(i,a);case 3:return e.abrupt("return",e.sent);case 4:case"end":return e.stop()}}),e)})));return function(t){return e.apply(this,arguments)}}();return[(0,i.useQuery)(["image",e,t],(function(){return d(e,t)}),{refetchOnMount:!1,refetchOnWindowFocus:!1,staleTime:1/0}),(0,i.useMutation)(m,{onSuccess:function(e,t){var n;t.fileId?c.removeQueries(["image",t.fileId,null!==(n=t.targetDrive)&&void 0!==n?n:l]):c.removeQueries(["image"])}}),(0,i.useMutation)(f,{onSuccess:function(e,t){var n;t.fileId?c.removeQueries(["image",t.fileId,null!==(n=t.targetDrive)&&void 0!==n?n:l]):c.removeQueries(["image"])}})]}},9077:function(e,t,n){var r=n(4165),a=n(5861),i=n(7408),s=n(1803),o=n(6117),l=n(1695);t.Z=function(e){var t=e.profileId,n=e.attributeId,c=(0,o.Z)().getSharedSecret,u=new s.KU({api:s.Ii.Owner,sharedSecret:c()}),d=(0,i.useQueryClient)(),m=(0,l.Z)().publish.mutate,f=function(){var e=(0,a.Z)((0,r.Z)().mark((function e(t,n){var a;return(0,r.Z)().wrap((function(e){for(;;)switch(e.prev=e.next){case 0:if(t&&n){e.next=2;break}return e.abrupt("return");case 2:return e.next=4,u.profileDataProvider.getAttribute(t,n);case 4:return a=e.sent,e.abrupt("return",a);case 6:case"end":return e.stop()}}),e)})));return function(t,n){return e.apply(this,arguments)}}(),x=function(){var e=(0,a.Z)((0,r.Z)().mark((function e(t){return(0,r.Z)().wrap((function(e){for(;;)switch(e.prev=e.next){case 0:return e.next=2,u.profileDataProvider.saveAttribute(t);case 2:case"end":return e.stop()}}),e)})));return function(t){return e.apply(this,arguments)}}(),h=function(){var e=(0,a.Z)((0,r.Z)().mark((function e(t){return(0,r.Z)().wrap((function(e){for(;;)switch(e.prev=e.next){case 0:if(!t.fileId){e.next=5;break}return e.next=3,u.profileDataProvider.removeAttribute(t.sectionId,t.fileId);case 3:e.next=6;break;case 5:console.log("error...");case 6:case"end":return e.stop()}}),e)})));return function(t){return e.apply(this,arguments)}}();return[(0,i.useQuery)(["attribute",t,n],(function(){return f(t,n)}),{refetchOnMount:!1,refetchOnWindowFocus:!1}),(0,i.useMutation)(x,{onSuccess:function(e,t){t.id?d.invalidateQueries(["attribute",t.profileId,t.id]):d.invalidateQueries(["attribute"]),d.removeQueries(["attributes",t.profileId,t.sectionId]),m()}}),(0,i.useMutation)(h,{onSuccess:function(e,t){t.id?d.invalidateQueries(["attribute",t.profileId,t.id]):d.invalidateQueries(["attribute"]),d.removeQueries(["attributes",t.profileId,t.sectionId]),m()}})]}},3431:function(e,t,n){var r=n(4165),a=n(1413),i=n(5861),s=n(7408),o=n(1803),l=n(3990),c=n(6117);t.Z=function(){var e=(0,c.Z)().getSharedSecret,t=function(){var t=(0,i.Z)((0,r.Z)().mark((function t(){var n,i;return(0,r.Z)().wrap((function(t){for(;;)switch(t.prev=t.next){case 0:return n=new o.KU({api:o.Ii.Owner,sharedSecret:e()}),t.next=3,n.profileDefinitionProvider.getProfileDefinitions();case 3:return t.next=5,t.sent.map((function(e){return(0,a.Z)((0,a.Z)({},e),{},{slug:(0,l.V)(e.name)})}));case 5:return i=t.sent,t.abrupt("return",{definitions:i});case 7:case"end":return t.stop()}}),t)})));return function(){return t.apply(this,arguments)}}();return(0,s.useQuery)(["profiles"],(function(){return t()}),{refetchOnMount:!1,refetchOnWindowFocus:!1})}},4942:function(e,t,n){function r(e,t,n){return t in e?Object.defineProperty(e,t,{value:n,enumerable:!0,configurable:!0,writable:!0}):e[t]=n,e}n.d(t,{Z:function(){return r}})},1413:function(e,t,n){n.d(t,{Z:function(){return i}});var r=n(4942);function a(e,t){var n=Object.keys(e);if(Object.getOwnPropertySymbols){var r=Object.getOwnPropertySymbols(e);t&&(r=r.filter((function(t){return Object.getOwnPropertyDescriptor(e,t).enumerable}))),n.push.apply(n,r)}return n}function i(e){for(var t=1;t<arguments.length;t++){var n=null!=arguments[t]?arguments[t]:{};t%2?a(Object(n),!0).forEach((function(t){(0,r.Z)(e,t,n[t])})):Object.getOwnPropertyDescriptors?Object.defineProperties(e,Object.getOwnPropertyDescriptors(n)):a(Object(n)).forEach((function(t){Object.defineProperty(e,t,Object.getOwnPropertyDescriptor(n,t))}))}return e}},2878:function(e,t,n){n.d(t,{Z:function(){return D}});var r=function(e){var t=typeof e;return null!=e&&("object"==t||"function"==t)},a="object"==typeof global&&global&&global.Object===Object&&global,i="object"==typeof self&&self&&self.Object===Object&&self,s=a||i||Function("return this")(),o=function(){return s.Date.now()},l=/\s/;var c=function(e){for(var t=e.length;t--&&l.test(e.charAt(t)););return t},u=/^\s+/;var d=function(e){return e?e.slice(0,c(e)+1).replace(u,""):e},m=s.Symbol,f=Object.prototype,x=f.hasOwnProperty,h=f.toString,g=m?m.toStringTag:void 0;var p=function(e){var t=x.call(e,g),n=e[g];try{e[g]=void 0;var r=!0}catch(i){}var a=h.call(e);return r&&(t?e[g]=n:delete e[g]),a},v=Object.prototype.toString;var b=function(e){return v.call(e)},j=m?m.toStringTag:void 0;var y=function(e){return null==e?void 0===e?"[object Undefined]":"[object Null]":j&&j in Object(e)?p(e):b(e)};var C=function(e){return null!=e&&"object"==typeof e};var w=function(e){return"symbol"==typeof e||C(e)&&"[object Symbol]"==y(e)},N=/^[-+]0x[0-9a-f]+$/i,Z=/^0b[01]+$/i,k=/^0o[0-7]+$/i,S=parseInt;var I=function(e){if("number"==typeof e)return e;if(w(e))return NaN;if(r(e)){var t="function"==typeof e.valueOf?e.valueOf():e;e=r(t)?t+"":t}if("string"!=typeof e)return 0===e?e:+e;e=d(e);var n=Z.test(e);return n||k.test(e)?S(e.slice(2),n?2:8):N.test(e)?NaN:+e},O=Math.max,F=Math.min;var D=function(e,t,n){var a,i,s,l,c,u,d=0,m=!1,f=!1,x=!0;if("function"!=typeof e)throw new TypeError("Expected a function");function h(t){var n=a,r=i;return a=i=void 0,d=t,l=e.apply(r,n)}function g(e){return d=e,c=setTimeout(v,t),m?h(e):l}function p(e){var n=e-u;return void 0===u||n>=t||n<0||f&&e-d>=s}function v(){var e=o();if(p(e))return b(e);c=setTimeout(v,function(e){var n=t-(e-u);return f?F(n,s-(e-d)):n}(e))}function b(e){return c=void 0,x&&a?h(e):(a=i=void 0,l)}function j(){var e=o(),n=p(e);if(a=arguments,i=this,u=e,n){if(void 0===c)return g(u);if(f)return clearTimeout(c),c=setTimeout(v,t),h(u)}return void 0===c&&(c=setTimeout(v,t)),l}return t=I(t)||0,r(n)&&(m=!!n.leading,s=(f="maxWait"in n)?O(I(n.maxWait)||0,t):s,x="trailing"in n?!!n.trailing:x),j.cancel=function(){void 0!==c&&clearTimeout(c),d=0,a=u=i=c=void 0},j.flush=function(){return void 0===c?l:b(o())},j}}}]);
//# sourceMappingURL=755.b66027fd.chunk.js.map