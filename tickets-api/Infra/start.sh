kind create cluster --name tickets-api-cluster --config tickets-api-cluster.yaml

kubectl apply -f tickets-api-deployment.yaml

kubectl port-forward svc/tickets-api-service 5000:80

