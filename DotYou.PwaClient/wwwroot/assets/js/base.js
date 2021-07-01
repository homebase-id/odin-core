//-----------------------------------------------------------------------
// Service Workers
//-----------------------------------------------------------------------
if ('serviceWorker' in navigator) {
    navigator.serviceWorker.register('service-worker.js')
        .then(reg => console.log('service worker registered'))
        .catch(err => console.log('service worker not registered - there is an error.', err));
}
//-----------------------------------------------------------------------


//-----------------------------------------------------------------------
// Page Loader with preload
//----------------------------------------------------------------------
document.addEventListener('DOMContentLoaded', function () {
    var loader = document.getElementById("loader");
    if(loader == null)
    {
        return;
    }

    setTimeout(() => {
        var loaderOpacity = 1;
        var fadeAnimation = setInterval(() => {
            if (loaderOpacity <= 0.05) {
                clearInterval(fadeAnimation);
                loader.style.opacity = "0";
                loader.style.display = "none";
            }
            loader.style.opacity = loaderOpacity;
            loader.style.filter = "alpha(opacity=" + loaderOpacity * 100 + ")";
            loaderOpacity = loaderOpacity - loaderOpacity * 0.5;
        }, 30);
    }, 700);
})
//-----------------------------------------------------------------------


//-----------------------------------------------------------------------
// Fix for # href
//-----------------------------------------------------------------------
var aWithHref = document.querySelectorAll('a[href*="#"]');
aWithHref.forEach(function (el) {
    el.addEventListener("click", function (e) {
        e.preventDefault();
    })
});
//-----------------------------------------------------------------------


//-----------------------------------------------------------------------
// Go Top Button
//-----------------------------------------------------------------------
var goTopButton = document.querySelectorAll(".goTop");
goTopButton.forEach(function (el) {
    // show fixed button after some scrolling
    window.addEventListener("scroll", function () {
        var scrolled = window.scrollY;
        if (scrolled > 100) {
            el.classList.add("show")
        }
        else {
            el.classList.remove("show")
        }
    })
    // go top on click
    el.addEventListener("click", function (e) {
        e.preventDefault();
        window.scrollTo({
            top: 0,
            behavior: 'smooth'
        });
    })

})
//-----------------------------------------------------------------------


//-----------------------------------------------------------------------
// Go Back Button
var goBackButton = document.querySelectorAll(".goBack");
goBackButton.forEach(function (el) {
    el.addEventListener("click", function () {
        window.history.go(-1);
    })
})
//-----------------------------------------------------------------------


//-----------------------------------------------------------------------
// Adbox Close
var adboxCloseButton = document.querySelectorAll(".adbox .closebutton");
adboxCloseButton.forEach(function (el) {
    el.addEventListener("click", function () {
        var adbox = this.parentElement
        adbox.classList.add("hide");
    })
})
//-----------------------------------------------------------------------


//-----------------------------------------------------------------------
// Copyright Year
var date = new Date();
var nowYear = date.getFullYear();
var copyrightYear = document.querySelector('.yearNow');
if (copyrightYear) {
    copyrightYear.innerHTML = nowYear
}
//-----------------------------------------------------------------------


//-----------------------------------------------------------------------
// Stories Component
var storiesButton = document.querySelectorAll("[data-component='stories']");
storiesButton.forEach(function (el) {
    el.addEventListener("click", function () {
        var target = this.getAttribute("data-bs-target");
        var content = document.querySelector(target + " .modal-content");
        var storytime = this.getAttribute("data-time");
        target = document.querySelector(target);
        if (storytime) {
            target.classList.add("with-story-bar");
            content.appendChild(document.createElement("div")).className = "story-bar";
            var storybar = document.querySelector("#" + target.id + " .story-bar")
            storybar.innerHTML = "<span></span>";
            //
            document.querySelector("#" + target.id + " .story-bar span").animate({
                width: '100%'
            }, storytime)

            var storyTimeout = setTimeout(() => {
                var modalEl = document.getElementById(target.id)
                var modal = bootstrap.Modal.getInstance(modalEl)
                modal.hide();
                storybar.remove();
                target.classList.remove("with-story-bar");
            }, storytime);

            var closeButton = document.querySelectorAll(".close-stories")
            closeButton.forEach(function (el) {
                el.addEventListener("click", function () {
                    clearTimeout(storyTimeout);
                    storybar.remove();
                    target.classList.remove("with-story-bar");
                })
            })

        }
    })
})
//-----------------------------------------------------------------------


//-----------------------------------------------------------------------
// OS Detection
var osDetection = navigator.userAgent || navigator.vendor || window.opera;
var windowsPhoneDetection = /windows phone/i.test(osDetection);
var androidDetection = /android/i.test(osDetection);
var iosDetection = /iPad|iPhone|iPod/.test(osDetection) && !window.MSStream;

var detectionWindowsPhone = document.querySelectorAll(".windowsphone-detection");
var detectionAndroid = document.querySelectorAll(".android-detection");
var detectioniOS = document.querySelectorAll(".ios-detection");
var detectionNone = document.querySelectorAll(".non-mobile-detection");

if (windowsPhoneDetection) {
    // Windows Phone Detected
    detectionWindowsPhone.forEach(function (el) {
        el.classList.add("is-active");
    })
}
else if (androidDetection) {
    // Android Detected
    detectionAndroid.forEach(function (el) {
        el.classList.add("is-active");
    })
}
else if (iosDetection) {
    // iOS Detected
    detectioniOS.forEach(function (el) {
        el.classList.add("is-active");
    })
}
else {
    // Non-Mobile Detected
    detectionNone.forEach(function (el) {
        el.classList.add("is-active");
    })

}
//-----------------------------------------------------------------------


//-----------------------------------------------------------------------
// Tooltip
var tooltipTriggerList = [].slice.call(document.querySelectorAll('[data-bs-toggle="tooltip"]'))
var tooltipList = tooltipTriggerList.map(function (tooltipTriggerEl) {
    return new bootstrap.Tooltip(tooltipTriggerEl)
})
//-----------------------------------------------------------------------


//-----------------------------------------------------------------------
// Input
// Clear input
var clearInput = document.querySelectorAll(".clear-input");
clearInput.forEach(function (el) {
    el.addEventListener("click", function () {
        var parent = this.parentElement
        var input = parent.querySelector(".form-control")
        input.focus();
        input.value = "";
        parent.classList.remove("not-empty");
    })
})
// active
var formControl = document.querySelectorAll(".form-group .form-control");
formControl.forEach(function (el) {
    // active
    el.addEventListener("focus", () => {
        var parent = el.parentElement
        parent.classList.add("active")
    });
    el.addEventListener("blur", () => {
        var parent = el.parentElement
        parent.classList.remove("active")
    });
    // empty check
    el.addEventListener("keyup", log);
    function log(e) {
        var inputCheck = this.value.length;
        if (inputCheck > 0) {
            this.parentElement.classList.add("not-empty")
        }
        else {
            this.parentElement.classList.remove("not-empty")
        }
    }
})
//-----------------------------------------------------------------------


//-----------------------------------------------------------------------
// Searchbox Toggle
var searchboxToggle = document.querySelectorAll(".toggle-searchbox")
searchboxToggle.forEach(function (el) {
    el.addEventListener("click", function () {
        var search = document.getElementById("search")
        var a = search.classList.contains("show")
        if (a) {
            search.classList.remove("show")
        }
        else {
            search.classList.add("show")
            search.querySelector(".form-control").focus();
        }
    })
});
//-----------------------------------------------------------------------


//-----------------------------------------------------------------------
// Stepper
var stepperUp = document.querySelectorAll(".stepper-up");
stepperUp.forEach(function (el) {
    el.addEventListener("click", function () {
        var input = el.parentElement.querySelector(".form-control");
        input.value = parseInt(input.value) + 1
    })
})
var stepperDown = document.querySelectorAll(".stepper-down");
stepperDown.forEach(function (el) {
    el.addEventListener("click", function () {
        var input = el.parentElement.querySelector(".form-control");
        if (parseInt(input.value) > 0) {
            input.value = parseInt(input.value) - 1
        }
    })
})
//-----------------------------------------------------------------------


//-----------------------------------------------------------------------
// Carousel
// Splide Carousel
document.addEventListener('DOMContentLoaded', function () {

    // Full Carousel
    document.querySelectorAll('.carousel-full').forEach(carousel => new Splide(carousel, {
        perPage: 1,
        rewind: true,
        type: "loop",
        gap: 0,
        arrows: false,
        pagination: false,
    }).mount());

    // Single Carousel
    document.querySelectorAll('.carousel-single').forEach(carousel => new Splide(carousel, {
        perPage: 3,
        rewind: true,
        type: "loop",
        gap: 16,
        padding: 16,
        arrows: false,
        pagination: false,
        breakpoints: {
            768: {
                perPage: 1
            },
            991: {
                perPage: 2
            }
        }
    }).mount());

    // Multiple Carousel
    document.querySelectorAll('.carousel-multiple').forEach(carousel => new Splide(carousel, {
        perPage: 4,
        rewind: true,
        type: "loop",
        gap: 16,
        padding: 16,
        arrows: false,
        pagination: false,
        breakpoints: {
            768: {
                perPage: 2
            },
            991: {
                perPage: 3
            }
        }
    }).mount());

    // Small Carousel
    document.querySelectorAll('.carousel-small').forEach(carousel => new Splide(carousel, {
        perPage: 9,
        rewind: false,
        type: "loop",
        gap: 16,
        padding: 16,
        arrows: false,
        pagination: false,
        breakpoints: {
            768: {
                perPage: 5
            },
            991: {
                perPage: 7
            }
        }
    }).mount());

    // Slider Carousel
    document.querySelectorAll('.carousel-slider').forEach(carousel => new Splide(carousel, {
        perPage: 1,
        rewind: false,
        type: "loop",
        gap: 16,
        padding: 16,
        arrows: false,
        pagination: true
    }).mount());

    // Stories Carousel
    document.querySelectorAll('.story-block').forEach(carousel => new Splide(carousel, {
        perPage: 16,
        rewind: false,
        type: "slide",
        gap: 16,
        padding: 16,
        arrows: false,
        pagination: false,
        breakpoints: {
            500: {
                perPage: 4
            },
            768: {
                perPage: 7
            },
            1200: {
                perPage: 11
            }
        }
    }).mount());
});
//-----------------------------------------------------------------------


//-----------------------------------------------------------------------
// Notification
// trigger notification
var notificationCloseButton = document.querySelectorAll(".notification-box .close-button");
var notificationTaptoClose = document.querySelectorAll(".tap-to-close .notification-dialog");
var notificationBox = document.querySelectorAll(".notification-box");

function closeNotificationBox() {
    notificationBox.forEach(function (el) {
        el.classList.remove("show")
    })
}
function notification(target, time) {
    var a = document.getElementById(target);
    closeNotificationBox()
    setTimeout(() => {
        a.classList.add("show")
    }, 250);
    if (time) {
        time = time + 250;
        setTimeout(() => {
            closeNotificationBox()
        }, time);
    }
}
// close notification
notificationCloseButton.forEach(function (el) {
    el.addEventListener("click", function (e) {
        e.preventDefault();
        closeNotificationBox();
    })
});

// tap to close notification
notificationTaptoClose.forEach(function (el) {
    el.addEventListener("click", function (e) {
        closeNotificationBox();
    })
});
//-----------------------------------------------------------------------


//-----------------------------------------------------------------------
// Toast
// trigger toast
var toastCloseButton = document.querySelectorAll(".toast-box .close-button");
var toastTaptoClose = document.querySelectorAll(".toast-box.tap-to-close");
var toastBoxes = document.querySelectorAll(".toast-box");

function closeToastBox() {
    toastBoxes.forEach(function (el) {
        el.classList.remove("show")
    })
}
function toastbox(target, time) {
    var a = document.getElementById(target);
    closeToastBox()
    setTimeout(() => {
        a.classList.add("show")
    }, 100);
    if (time) {
        time = time + 100;
        setTimeout(() => {
            closeToastBox()
        }, time);
    }
}
// close button toast
toastCloseButton.forEach(function (el) {
    el.addEventListener("click", function (e) {
        e.preventDefault();
        closeToastBox();
    })
})
// tap to close toast
toastTaptoClose.forEach(function (el) {
    el.addEventListener("click", function (e) {
        closeToastBox();
    })
})
//-----------------------------------------------------------------------


//-----------------------------------------------------------------------
// Header Scrolled
// Animated header style
var appHeader = document.querySelector(".appHeader.scrolled");
function animatedScroll() {
    var scrolled = window.scrollY;
    if (scrolled > 20) {
        appHeader.classList.add("is-active")
    }
    else {
        appHeader.classList.remove("is-active")
    }
}
if (document.body.contains(appHeader)) {
    animatedScroll();
    window.addEventListener("scroll", function () {
        animatedScroll();
    })
}
//-----------------------------------------------------------------------


//-----------------------------------------------------------------------
// Offline Mode / Online Mode Detection

// You can change the text here
var OnlineText = "Connected to Internet";
var OfflineText = "No Internet Connection";

// Online Mode Toast Append
function onlineModeToast() {
    var check = document.getElementById("online-toast");
    if (document.body.contains(check)) {
        check.classList.add("show")
    }
    else {
        pageBody.appendChild(document.createElement("div")).id = "online-toast";
        var toast = document.getElementById("online-toast");
        toast.className = "toast-box bg-success toast-top tap-to-close";
        toast.innerHTML =
            "<div class='in'><div class='text'>"
            +
            OnlineText
            +
            "</div></div>"
        setTimeout(() => {
            toastbox('online-toast', 3000);
        }, 500);
    }
}

// Offline Mode Toast Append
function offlineModeToast() {
    var check = document.getElementById("offline-toast");
    if (document.body.contains(check)) {
        check.classList.add("show")
    }
    else {
        pageBody.appendChild(document.createElement("div")).id = "offline-toast";
        var toast = document.getElementById("offline-toast");
        toast.className = "toast-box bg-danger toast-top tap-to-close";
        toast.innerHTML =
            "<div class='in'><div class='text'>"
            +
            OfflineText
            +
            "</div></div>"
        setTimeout(() => {
            toastbox('offline-toast', 3000);
        }, 500);
    }
}

// Online Mode Function
function onlineMode() {
    var check = document.getElementById("offline-toast");
    if (document.body.contains(check)) {
        check.classList.remove("show")
    }
    onlineModeToast();
    var toast = document.getElementById("online-toast")
    toast.addEventListener("click", function () {
        this.classList.remove("show")
    })
    setTimeout(() => {
        toast.classList.remove("show")
    }, 3000);
}

// Online Mode Function
function offlineMode() {
    var check = document.getElementById("online-toast");
    if (document.body.contains(check)) {
        check.classList.remove("show")
    }
    offlineModeToast();
    var toast = document.getElementById("offline-toast")
    toast.addEventListener("click", function () {
        this.classList.remove("show")
    })
    setTimeout(() => {
        toast.classList.remove("show")
    }, 3000);
}

// Check with event listener if online or offline
window.addEventListener('online', onlineMode);
window.addEventListener('offline', offlineMode);
//-----------------------------------------------------------------------


//-----------------------------------------------------------------------
// Upload Input
var uploadComponent = document.querySelectorAll('.custom-file-upload');
uploadComponent.forEach(function (el) {
    var fileUploadParent = '#' + el.id;
    var fileInput = document.querySelector(fileUploadParent + ' input[type="file"]')
    var fileLabel = document.querySelector(fileUploadParent + ' label')
    var fileLabelText = document.querySelector(fileUploadParent + ' label span')
    var filelabelDefault = fileLabelText.innerHTML;
    fileInput.addEventListener('change', function (event) {
        var name = this.value.split('\\').pop()
        tmppath = URL.createObjectURL(event.target.files[0]);
        if (name) {
            fileLabel.classList.add('file-uploaded');
            fileLabel.style.backgroundImage = "url(" + tmppath + ")";
            fileLabelText.innerHTML = name;
        }
        else {
            fileLabel.classList.remove("file-uploaded")
            fileLabelText.innerHTML = filelabelDefault;
        }
    })
})
//-----------------------------------------------------------------------


//-----------------------------------------------------------------------
// Multi-level Listview
var multiListview = document.querySelectorAll(".listview .multi-level > a.item");

multiListview.forEach(function (el) {
    el.addEventListener("click", function () {
        var parent = this.parentNode;
        var listview = parent.parentNode;
        var container = parent.querySelectorAll('.listview')
        var activated = listview.querySelectorAll('.multi-level.active');
        var activatedContainer = listview.querySelectorAll('.multi-level.active .listview')

        function openContainer() {
            container.forEach(function (e) {
                e.style.height = 'auto';
                var currentheight = e.clientHeight + 10 + 'px';
                e.style.height = '0px'
                setTimeout(() => {
                    e.style.height = currentheight
                }, 0);
            })
        }
        function closeContainer() {
            container.forEach(function (e) {
                e.style.height = '0px';
            })
        }
        if (parent.classList.contains('active')) {
            parent.classList.remove('active');
            closeContainer();
        }
        else {
            parent.classList.add('active');
            openContainer();
        }
        activated.forEach(function (element) {
            element.classList.remove('active');
            activatedContainer.forEach(function (e) {
                e.style.height = '0px'
            })
        })
    });

})
//-----------------------------------------------------------------------


//-----------------------------------------------------------------------
// Add to Home
function iosAddtoHome() {
    var modal = new bootstrap.Modal(document.getElementById('ios-add-to-home-screen'))
    modal.toggle()
}
function androidAddtoHome() {
    var modal = new bootstrap.Modal(document.getElementById('android-add-to-home-screen'))
    modal.toggle()
}
function AddtoHome(time, once) {
    if (once) {
        var AddHomeStatus = localStorage.getItem("MobilekitAddHomeStatus");
        if (AddHomeStatus === "1" || AddHomeStatus === 1) {
            // already showed up
        }
        else {
            localStorage.setItem("MobilekitAddHomeStatus", 1)
            window.addEventListener('load', () => {
                if (navigator.standalone) {
                    // if app installed ios home screen
                }
                else if (matchMedia('(display-mode: standalone)').matches) {
                    // if app installed android home screen
                }
                else {
                    // if app is not installed
                    if (androidDetection) {
                        setTimeout(() => {
                            androidAddtoHome()
                        }, time);
                    }
                    if (iosDetection) {
                        setTimeout(() => {
                            iosAddtoHome()
                        }, time);
                    }
                }
            });
        }
    }
    else {
        window.addEventListener('load', () => {
            if (navigator.standalone) {
                // app loaded to ios
            }
            else if (matchMedia('(display-mode: standalone)').matches) {
                // app loaded to android
            }
            else {
                // app not loaded
                if (androidDetection) {
                    setTimeout(() => {
                        androidAddtoHome()
                    }, time);
                }
                if (iosDetection) {
                    setTimeout(() => {
                        iosAddtoHome()
                    }, time);
                }
            }
        });
    }

}
//-----------------------------------------------------------------------


//-----------------------------------------------------------------------
// Dark Mode Detection
var checkDarkModeStatus = localStorage.getItem("MobilekitDarkModeActive");
var switchDarkMode = document.querySelectorAll(".dark-mode-switch");
var pageBody = document.querySelector("body");
var pageBodyActive = pageBody.classList.contains("dark-mode-active");

function switchDarkModeCheck(value) {
    switchDarkMode.forEach(function (el) {
        el.checked = value
    })
}
// if dark mode on
if (checkDarkModeStatus === 1 || checkDarkModeStatus === "1") {
    switchDarkModeCheck(true);
    if (pageBodyActive) {
        // dark mode already activated
    }
    else {
        pageBody.classList.add("dark-mode-active")
    }
}
else {
    switchDarkModeCheck(false);
}
switchDarkMode.forEach(function (el) {
    el.addEventListener("click", function () {
        var darkmodeCheck = localStorage.getItem("MobilekitDarkModeActive");
        if (darkmodeCheck === 1 || darkmodeCheck === "1") {
            pageBody.classList.remove("dark-mode-active");
            localStorage.setItem("MobilekitDarkModeActive", "0");
            switchDarkModeCheck(false);
        }
        else {
            pageBody.classList.add("dark-mode-active")
            switchDarkModeCheck(true);
            localStorage.setItem("MobilekitDarkModeActive", "1");
        }
    })
})
//-----------------------------------------------------------------------


//-----------------------------------------------------------------------
// Countdown
function countdownTimer(time) {
    var end = time;
    end = new Date(end).getTime();
    var d, h, m, s;
    setInterval(() => {
        let now = new Date().getTime();
        let r = parseInt((end - now) / 1000);
        if (r >= 0) {
            // days
            d = parseInt(r / 86400);
            r = (r % 86400);
            // hours
            h = parseInt(r / 3600);
            r = (r % 3600);
            // minutes
            m = parseInt(r / 60);
            r = (r % 60);
            // seconds
            s = parseInt(r);
            d = parseInt(d, 10);
            h = h < 10 ? "0" + h : h;
            m = m < 10 ? "0" + m : m;
            s = s < 10 ? "0" + s : s;
            document.getElementById("countDown").innerHTML =
                "<div>" + d + "<span>Days</span></div>"
                +
                "<div>" + h + "<span>Hours</span></div>"
                +
                "<div>" + m + "<span>Minutes</span></div>"
                +
                "<div>" + s + "<span>Seconds</span></div>"
        } else {
            document.getElementById("countDown").innerHTML = "<p class='alert alert-outline-warning'>The countdown is over.</p>"
        }
    }, 1000);
}
//-----------------------------------------------------------------------