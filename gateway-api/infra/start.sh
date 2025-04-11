kind create cluster --name gateway-api-cluster --config gateway-api-cluster.yaml

kubectl apply -f gateway-api-deployment.yaml

kubectl port-forward svc/gateway-api-service 3000:80

